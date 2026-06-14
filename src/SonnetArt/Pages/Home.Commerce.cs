using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using SonnetArt.Models;

namespace SonnetArt.Pages;

public partial class Home
{
    private bool _commerceProductDialogOpen;
    private string? _commerceEditingProductId;
    private string _commerceProductName = string.Empty;
    private string _commerceProductDescription = string.Empty;
    private string _commerceProductSellingPoints = string.Empty;
    private string _commerceProductSpecifications = string.Empty;
    private string _commerceProductTargetAudience = string.Empty;
    private string _commerceProductReferenceImages = string.Empty;
    private string _commerceSkuVariants = string.Empty;
    private string? _commerceProductError;
    private string? _commerceAnalysisMessage;
    private bool _commerceAnalysisIsError;
    private const int CommerceMaxReferenceImages = 16;
    private const long CommerceMaxReferenceFileSize = 12 * 1024 * 1024;

    private CommerceProduct? ActiveCommerceProduct =>
        CommerceWorkspace.Products.FirstOrDefault(product => product.Id == CommerceWorkspace.ActiveProductId)
        ?? CommerceWorkspace.Products.OrderByDescending(product => product.UpdatedAt).FirstOrDefault();

    private CommerceImagePlan? ActiveCommercePlan =>
        CommerceWorkspace.ImagePlans.FirstOrDefault(plan => plan.Id == CommerceWorkspace.ActiveImagePlanId)
        ?? CommerceWorkspace.ImagePlans.FirstOrDefault(plan => plan.ProductId == ActiveCommerceProduct?.Id)
        ?? CommerceWorkspace.ImagePlans.OrderByDescending(plan => plan.UpdatedAt).FirstOrDefault();

    private IReadOnlyList<CommerceImageNode> CommercePlanNodes =>
        ActiveCommercePlan?.Nodes.Count > 0
            ? ActiveCommercePlan.Nodes
            : _commerceEmptyNodes.Select(node => new CommerceImageNode
            {
                Title = node.Title,
                Goal = node.Description,
                Status = node.Status,
            }).ToArray();

    private bool CommerceCanGeneratePlan => ActiveCommerceProduct is not null;
    private bool CommerceCanAnalyzeProduct => ActiveCommerceProduct is not null && !_loading;
    private string CommerceProductDialogTitle => _commerceEditingProductId is null ? "新增商品" : "编辑商品";
    private string CommerceProductDialogAction => _commerceEditingProductId is null ? "保存商品" : "保存修改";
    private bool CommerceProductSubmitDisabled => string.IsNullOrWhiteSpace(_commerceProductName);
    private string? CommerceAnalysisMessage => _commerceAnalysisMessage;
    private bool CommerceAnalysisIsError => _commerceAnalysisIsError;

    private Task OpenNewCommerceProduct()
    {
        _commerceEditingProductId = null;
        _commerceProductName = string.Empty;
        _commerceProductDescription = string.Empty;
        _commerceProductSellingPoints = string.Empty;
        _commerceProductSpecifications = string.Empty;
        _commerceProductTargetAudience = string.Empty;
        _commerceProductReferenceImages = string.Empty;
        _commerceSkuVariants = string.Empty;
        _commerceProductError = null;
        _commerceProductDialogOpen = true;
        return Task.CompletedTask;
    }

    private Task OpenEditCommerceProduct(CommerceProduct product)
    {
        _commerceEditingProductId = product.Id;
        _commerceProductName = product.Name;
        _commerceProductDescription = product.Description;
        _commerceProductSellingPoints = string.Join(Environment.NewLine, product.SellingPoints);
        _commerceProductSpecifications = product.Specifications;
        _commerceProductTargetAudience = product.TargetAudience;
        _commerceProductReferenceImages = string.Join(Environment.NewLine, product.ReferenceImages);
        _commerceSkuVariants = string.Join(Environment.NewLine, product.SkuVariants.Select(FormatCommerceSkuVariant));
        _commerceProductError = null;
        _commerceProductDialogOpen = true;
        return Task.CompletedTask;
    }

    private Task CloseCommerceProductDialog()
    {
        _commerceProductDialogOpen = false;
        _commerceProductError = null;
        return Task.CompletedTask;
    }

    private async Task SelectCommerceProduct(string productId)
    {
        if (CommerceWorkspace.Products.All(product => product.Id != productId))
        {
            return;
        }

        CommerceWorkspace.ActiveProductId = productId;
        CommerceWorkspace.ActiveImagePlanId = CommerceWorkspace.ImagePlans
            .OrderByDescending(plan => plan.UpdatedAt)
            .FirstOrDefault(plan => plan.ProductId == productId)?.Id;
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task SaveCommerceProduct()
    {
        var name = _commerceProductName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _commerceProductError = "请填写商品名称。";
            return;
        }

        var now = DateTimeOffset.Now;
        var product = _commerceEditingProductId is null
            ? null
            : CommerceWorkspace.Products.FirstOrDefault(item => item.Id == _commerceEditingProductId);

        if (product is null)
        {
            product = new CommerceProduct
            {
                CreatedAt = now,
            };
            CommerceWorkspace.Products.Insert(0, product);
        }

        product.Name = name;
        product.Description = _commerceProductDescription;
        product.SellingPoints = SplitCommerceLines(_commerceProductSellingPoints).ToList();
        product.Specifications = _commerceProductSpecifications;
        product.TargetAudience = _commerceProductTargetAudience;
        product.ReferenceImages = SplitCommerceReferenceImages(_commerceProductReferenceImages).ToList();
        product.ReferenceRole = "product";
        product.SkuVariants = ParseCommerceSkuVariants(_commerceSkuVariants).ToList();
        product.UpdatedAt = now;
        product.Normalize();

        CommerceWorkspace.ActiveProductId = product.Id;
        SyncCommerceProductReferencesToActiveSession(product);
        TouchCommerceWorkspace();
        _commerceProductDialogOpen = false;
        _commerceProductError = null;
        await SaveAsync();
    }

    private async Task DeleteCommerceProduct(CommerceProduct product)
    {
        CommerceWorkspace.Products.RemoveAll(item => item.Id == product.Id);
        CommerceWorkspace.ImagePlans.RemoveAll(plan => plan.ProductId == product.Id);

        var nextProduct = CommerceWorkspace.Products.OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
        CommerceWorkspace.ActiveProductId = nextProduct?.Id;
        CommerceWorkspace.ActiveImagePlanId = nextProduct is null
            ? null
            : CommerceWorkspace.ImagePlans
                .OrderByDescending(plan => plan.UpdatedAt)
                .FirstOrDefault(plan => plan.ProductId == nextProduct.Id)?.Id;

        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task GenerateCommercePlan()
    {
        var product = ActiveCommerceProduct;
        if (product is null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var plan = new CommerceImagePlan
        {
            ProductId = product.Id,
            Title = $"{product.Name} 首轮商品图方案",
            CreatedAt = now,
            UpdatedAt = now,
            Nodes = CreateCommercePlanNodes(product),
        };

        CommerceWorkspace.ImagePlans.RemoveAll(item => item.ProductId == product.Id);
        CommerceWorkspace.ImagePlans.Insert(0, plan);
        CommerceWorkspace.ActiveProductId = product.Id;
        CommerceWorkspace.ActiveImagePlanId = plan.Id;
        SyncCommerceProductReferencesToActiveSession(product);
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task AnalyzeCommerceProduct()
    {
        var product = ActiveCommerceProduct;
        if (product is null || _loading)
        {
            return;
        }

        if (!ChatReady)
        {
            _commerceAnalysisMessage = "请先登录账户，完成 GPT 会话能力配置后再分析商品。";
            _commerceAnalysisIsError = true;
            return;
        }

        _commerceAnalysisMessage = null;
        _commerceAnalysisIsError = false;
        _loading = true;
        _loadingLabel = "正在分析商品";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        StateHasChanged();

        try
        {
            var analysis = await ChatClient.AnalyzeCommerceProductAsync(Settings, product, _cts.Token);
            ApplyCommerceProductAnalysis(product, analysis);
            product.UpdatedAt = DateTimeOffset.Now;
            CommerceWorkspace.ActiveProductId = product.Id;
            SyncCommerceProductReferencesToActiveSession(product);
            TouchCommerceWorkspace();
            _commerceAnalysisMessage = "AI 商品分析已更新。";
            await SaveAsync();
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            _commerceAnalysisMessage = "本次商品分析已取消。";
            _commerceAnalysisIsError = true;
        }
        catch (Exception ex)
        {
            _commerceAnalysisMessage = ex.Message;
            _commerceAnalysisIsError = true;
        }
        finally
        {
            _loading = false;
            _loadingLabel = "正在请求图像接口";
            StateHasChanged();
        }
    }

    private static void ApplyCommerceProductAnalysis(CommerceProduct product, CommerceProductAnalysis analysis)
    {
        analysis.Normalize();
        product.Analysis = analysis;

        if (product.SellingPoints.Count == 0 && analysis.CoreSellingPoints.Count > 0)
        {
            product.SellingPoints = analysis.CoreSellingPoints.ToList();
        }

        if (string.IsNullOrWhiteSpace(product.TargetAudience) && analysis.TargetAudiences.Count > 0)
        {
            product.TargetAudience = string.Join("、", analysis.TargetAudiences.Take(3));
        }

        if (product.SkuVariants.Count == 0 && analysis.ColorVariants.Count > 0)
        {
            product.SkuVariants = analysis.ColorVariants
                .Select(color => new CommerceSkuVariant
                {
                    Name = color,
                    Color = color,
                })
                .ToList();
        }

        product.Normalize();
    }

    private void UpdateCommerceProductName(ChangeEventArgs args) => _commerceProductName = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductDescription(ChangeEventArgs args) => _commerceProductDescription = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductSellingPoints(ChangeEventArgs args) => _commerceProductSellingPoints = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductSpecifications(ChangeEventArgs args) => _commerceProductSpecifications = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductTargetAudience(ChangeEventArgs args) => _commerceProductTargetAudience = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductReferenceImages(ChangeEventArgs args) => _commerceProductReferenceImages = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceSkuVariants(ChangeEventArgs args) => _commerceSkuVariants = args.Value?.ToString() ?? string.Empty;

    private async Task AddCommerceProductReferenceFiles(InputFileChangeEventArgs args)
    {
        var references = SplitCommerceReferenceImages(_commerceProductReferenceImages).ToList();
        var remaining = Math.Max(0, CommerceMaxReferenceImages - references.Count);
        if (remaining == 0)
        {
            _commerceProductError = $"最多保留 {CommerceMaxReferenceImages} 张产品参考图。";
            return;
        }

        _commerceProductError = null;
        foreach (var file in args.GetMultipleFiles(CommerceMaxReferenceImages).Take(remaining))
        {
            if (!IsBrowserImageFile(file))
            {
                _commerceProductError = "目前只支持图片文件。";
                continue;
            }

            try
            {
                await using var stream = file.OpenReadStream(CommerceMaxReferenceFileSize);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                var reference = new ImageReferenceFile(file.Name, file.ContentType, memory.ToArray());
                references.Add(ToDataUrl(reference));
            }
            catch (IOException)
            {
                _commerceProductError = $"单张产品参考图不能超过 {CommerceMaxReferenceFileSize / 1024 / 1024}MB。";
            }
            catch (InvalidOperationException)
            {
                _commerceProductError = $"单张产品参考图不能超过 {CommerceMaxReferenceFileSize / 1024 / 1024}MB。";
            }
        }

        _commerceProductReferenceImages = string.Join(Environment.NewLine, references
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(CommerceMaxReferenceImages));
    }

    private string CommerceProductItemClass(CommerceProduct product) =>
        string.Equals(product.Id, ActiveCommerceProduct?.Id, StringComparison.Ordinal)
            ? "commerce-product-item active"
            : "commerce-product-item";

    private static string CommerceProductPreview(CommerceProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.Description))
        {
            return product.Description;
        }

        if (product.SellingPoints.Count > 0)
        {
            return string.Join(" / ", product.SellingPoints.Take(3));
        }

        return "未填写描述";
    }

    private static string CommerceProductMeta(CommerceProduct product) =>
        $"{product.SellingPoints.Count} 个卖点 · {product.SkuVariants.Count} 个 SKU · {product.ReferenceImages.Count} 张参考图";

    private static IEnumerable<string> SplitCommerceLines(string value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitCommerceReferenceImages(string value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(CommerceMaxReferenceImages);

    private static IEnumerable<CommerceSkuVariant> ParseCommerceSkuVariants(string value)
    {
        foreach (var line in SplitCommerceLines(value))
        {
            var parts = line
                .Split(['|', ',', '，', '\t'], StringSplitOptions.TrimEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            if (parts.Length == 0)
            {
                continue;
            }

            yield return new CommerceSkuVariant
            {
                Name = parts[0],
                Color = parts.Length > 1 ? parts[1] : string.Empty,
                Sku = parts.Length > 2 ? parts[2] : string.Empty,
            };
        }
    }

    private static string FormatCommerceSkuVariant(CommerceSkuVariant variant)
    {
        var parts = new[] { variant.Name, variant.Color, variant.Sku }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" | ", parts);
    }

    private static List<CommerceImageNode> CreateCommercePlanNodes(CommerceProduct product)
    {
        var facts = BuildCommerceProductFacts(product);
        const string negative = "避免改变产品结构、品牌信息错误、文字乱码、比例失真、廉价直播电商风、过度装饰、模糊细节。";

        return
        [
            CreateCommercePlanNode("main", "主图", "用于平台首图，清晰展示商品主体和核心外观。", "1:1", 4, facts, "干净白底或浅灰背景，商品居中，真实摄影质感，边缘清晰，可用于电商搜索列表。", negative),
            CreateCommercePlanNode("scene", "场景图", "展示目标人群的真实使用场景。", "4:3", 4, facts, "把商品放入符合目标人群的生活方式场景，保留产品比例和材质，光线自然，有明确购买想象。", negative),
            CreateCommercePlanNode("detail", "细节图", "放大材质、结构、工艺和关键卖点。", "1:1", 3, facts, "微距商品摄影，突出核心卖点、材质纹理、结构细节和使用触感，画面干净。", negative),
            CreateCommercePlanNode("compare", "对比图", "表达卖点、效果或规格差异。", "4:3", 2, facts, "采用克制的信息图构图，左右或上下对比，少量清晰标签，突出商品优势。", negative),
            CreateCommercePlanNode("size", "尺寸图", "说明尺寸、容量、包装规格或套装内容。", "1:1", 2, facts, "商品比例准确，加入简洁尺寸标注和规格信息，背景干净，信息层级清楚。", negative),
            CreateCommercePlanNode("aplus", "A+ 图", "用于详情页模块、品牌故事和卖点长图。", "3:4", 3, facts, "详情页模块式竖版视觉，包含商品主视觉、卖点区块和场景氛围，文字极少且清晰。", negative),
            CreateCommercePlanNode("package", "包装图", "展示包装、套装、赠品和到手内容。", "1:1", 2, facts, "商品与包装盒、套装配件整齐陈列，真实棚拍，适合平台详情页和活动页。", negative),
        ];
    }

    private static CommerceImageNode CreateCommercePlanNode(
        string type,
        string title,
        string goal,
        string aspectRatio,
        int plannedCount,
        string facts,
        string instruction,
        string negativePrompt)
    {
        return new CommerceImageNode
        {
            Type = type,
            Title = title,
            Goal = goal,
            AspectRatio = aspectRatio,
            PlannedCount = plannedCount,
            Status = "待确认",
            Prompt = $"{facts}{Environment.NewLine}{Environment.NewLine}图片任务：{instruction}",
            NegativePrompt = negativePrompt,
            ReferenceRole = "product",
            Enabled = true,
        };
    }

    private static string BuildCommerceProductFacts(CommerceProduct product)
    {
        var parts = new List<string> { $"商品名称：{product.Name}" };
        if (!string.IsNullOrWhiteSpace(product.Description))
        {
            parts.Add($"商品描述：{product.Description}");
        }

        if (product.SellingPoints.Count > 0)
        {
            parts.Add($"核心卖点：{string.Join("；", product.SellingPoints)}");
        }

        if (product.Analysis.HasContent)
        {
            if (!string.IsNullOrWhiteSpace(product.Analysis.ProductType))
            {
                parts.Add($"产品类型：{product.Analysis.ProductType}");
            }

            if (product.Analysis.CoreSellingPoints.Count > 0)
            {
                parts.Add($"AI 提炼卖点：{string.Join("；", product.Analysis.CoreSellingPoints)}");
            }

            if (product.Analysis.UseScenarios.Count > 0)
            {
                parts.Add($"适用场景：{string.Join("；", product.Analysis.UseScenarios)}");
            }

            if (product.Analysis.ColorVariants.Count > 0)
            {
                parts.Add($"颜色变体：{string.Join("；", product.Analysis.ColorVariants)}");
            }

            if (product.Analysis.MaterialFeatures.Count > 0)
            {
                parts.Add($"材质特性：{string.Join("；", product.Analysis.MaterialFeatures)}");
            }

            if (product.Analysis.TargetAudiences.Count > 0)
            {
                parts.Add($"AI 目标人群：{string.Join("；", product.Analysis.TargetAudiences)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(product.Specifications))
        {
            parts.Add($"规格信息：{product.Specifications}");
        }

        if (!string.IsNullOrWhiteSpace(product.TargetAudience))
        {
            parts.Add($"目标人群：{product.TargetAudience}");
        }

        if (product.SkuVariants.Count > 0)
        {
            parts.Add($"SKU 变体：{string.Join("；", product.SkuVariants.Select(FormatCommerceSkuVariant))}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private void TouchCommerceWorkspace()
    {
        CommerceWorkspace.UpdatedAt = DateTimeOffset.Now;
        TouchWorkspace(ActiveWorkspace);
    }

    private void SyncCommerceProductReferencesToActiveSession(CommerceProduct product)
    {
        ClearAllReferenceInputs();
        ActiveSession.ImageReferences = string.Join('\n', product.ReferenceImages);
        ActiveSession.MaskReference = string.Empty;
        ActiveSession.ReferenceRole = "product";
        ApplyResolvedMode(product.ReferenceImages.Count > 0 ? "image" : "generate");
        TouchActiveSession(ActiveSession.Prompt);
    }
}
