using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonnetArt.Models;

namespace SonnetArt.Pages;

public partial class Home
{
    private bool _commerceProductDialogOpen;
    private string? _commerceSelectedNodeId;
    private string? _commerceGeneratingNodeId;
    private PendingCommerceIteration? _commercePendingIteration;
    private PendingCommerceVariant? _commercePendingVariant;
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
    private string _commerceExportPlatform = "amazon";
    private string _commerceExportScope = "plan";
    private string _commerceExportImageSelection = "selected-or-all";
    private string _commerceExportResolutionTier = "source";
    private string _commerceExportFileNamePattern = "{sku}-{node}-{index}";
    private const int CommerceMaxReferenceImages = 16;
    private const long CommerceMaxReferenceFileSize = 12 * 1024 * 1024;
    private static readonly JsonSerializerOptions CommerceExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

    private CommerceImageNode? ActiveCommerceNode =>
        ActiveCommercePlan?.Nodes.FirstOrDefault(node => node.Id == _commerceSelectedNodeId)
        ?? ActiveCommercePlan?.Nodes.FirstOrDefault();

    private IReadOnlyList<GeneratedImage> CommerceNodeImages =>
        GetCommerceNodeImages(ActiveCommerceNode);

    private IReadOnlyList<GeneratedImage> CommercePlanImages =>
        ActiveCommercePlan is null
            ? []
            : ActiveCommercePlan.Nodes
                .SelectMany(GetCommerceNodeImages)
                .DistinctBy(image => image.Id)
                .OrderByDescending(image => image.CreatedAt)
                .ToArray();

    private bool CommerceCanGeneratePlan => ActiveCommerceProduct is not null && !_loading;
    private bool CommerceCanAnalyzeProduct => ActiveCommerceProduct is not null && !_loading;
    private bool CommerceCanIterateSelectedImage => ActiveCommerceNode is not null && ResolveCommerceIterationSourceImage(ActiveCommerceNode) is not null && !_loading;
    private bool CommerceCanApplyVariants => ActiveCommerceProduct?.SkuVariants.Count > 0 &&
        ActiveCommerceNode is not null &&
        ResolveCommerceIterationSourceImage(ActiveCommerceNode) is not null &&
        !_loading;
    private string CommerceProductDialogTitle => _commerceEditingProductId is null ? "新增商品" : "编辑商品";
    private string CommerceProductDialogAction => _commerceEditingProductId is null ? "保存商品" : "保存修改";
    private bool CommerceProductDialogCaptureOnly => _commerceEditingProductId is null;
    private bool CommerceProductDialogBusy => _loading && string.Equals(_loadingLabel, "正在识别商品", StringComparison.Ordinal);
    private bool CommerceProductSubmitDisabled => CommerceProductDialogBusy ||
        (_commerceEditingProductId is null
            ? string.IsNullOrWhiteSpace(_commerceProductReferenceImages)
            : string.IsNullOrWhiteSpace(_commerceProductName));
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
        _commerceSelectedNodeId = CommerceWorkspace.ImagePlans
            .FirstOrDefault(plan => plan.Id == CommerceWorkspace.ActiveImagePlanId)?
            .Nodes.FirstOrDefault()?.Id;
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
        if (product is null || _loading)
        {
            return;
        }

        if (!ChatReady)
        {
            CreateLocalCommercePlan(product, "内置规划已生成。登录后可使用 AI 规划。");
            await SaveAsync();
            return;
        }

        _commerceAnalysisMessage = null;
        _commerceAnalysisIsError = false;
        _loading = true;
        _loadingLabel = "正在规划商品图";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        StateHasChanged();

        try
        {
            var seedNodes = CreateCommercePlanNodes(product);
            var plan = await ChatClient.PlanCommerceImagesAsync(Settings, product, seedNodes, _cts.Token);
            ApplyCommercePlan(product, plan, $"{product.Name} AI 商品图方案");
            _commerceAnalysisMessage = "AI 图片规划已生成。";
            await SaveAsync();
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            _commerceAnalysisMessage = "本次图片规划已取消。";
            _commerceAnalysisIsError = true;
        }
        catch (Exception ex)
        {
            CreateLocalCommercePlan(product, $"AI 规划失败，已生成内置规划：{ex.Message}", isError: true);
            await SaveAsync();
        }
        finally
        {
            _loading = false;
            _loadingLabel = "正在请求图像接口";
            StateHasChanged();
        }
    }

    private void CreateLocalCommercePlan(CommerceProduct product, string? message = null, bool isError = false)
    {
        var now = DateTimeOffset.Now;
        var plan = new CommerceImagePlan
        {
            ProductId = product.Id,
            Title = $"{product.Name} 首轮商品图方案",
            StrategySummary = "围绕商品主体、使用场景、材质细节、规格说明和详情页转化建立首轮图片覆盖。",
            Model = "built-in",
            CreatedAt = now,
            UpdatedAt = now,
            Nodes = CreateCommercePlanNodes(product),
        };

        ApplyCommercePlan(product, plan, plan.Title);
        _commerceAnalysisMessage = message;
        _commerceAnalysisIsError = isError;
    }

    private void ApplyCommercePlan(CommerceProduct product, CommerceImagePlan plan, string fallbackTitle)
    {
        var now = DateTimeOffset.Now;
        plan.ProductId = product.Id;
        plan.Title = string.IsNullOrWhiteSpace(plan.Title) ? fallbackTitle : plan.Title;
        plan.CreatedAt = now;
        plan.UpdatedAt = now;
        plan.Normalize();

        if (plan.Nodes.Count == 0)
        {
            plan.Nodes = CreateCommercePlanNodes(product);
        }

        CommerceWorkspace.ImagePlans.RemoveAll(item => item.ProductId == product.Id);
        CommerceWorkspace.ImagePlans.Insert(0, plan);
        CommerceWorkspace.ActiveProductId = product.Id;
        CommerceWorkspace.ActiveImagePlanId = plan.Id;
        _commerceSelectedNodeId = plan.Nodes.FirstOrDefault()?.Id;
        SyncCommerceProductReferencesToActiveSession(product);
        TouchCommerceWorkspace();
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

    private async Task SelectCommercePlanNode(string nodeId)
    {
        if (ActiveCommercePlan?.Nodes.Any(node => node.Id == nodeId) != true)
        {
            return;
        }

        _commerceSelectedNodeId = nodeId;
        await Task.CompletedTask;
    }

    private async Task ToggleCommercePlanNode(CommerceImageNode node)
    {
        var plan = ActiveCommercePlan;
        if (plan is null)
        {
            return;
        }

        node.Enabled = !node.Enabled;
        node.Status = node.Enabled ? "已确认" : "已暂停";
        plan.UpdatedAt = DateTimeOffset.Now;
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task UpdateCommercePlanNode(CommerceImageNode updatedNode)
    {
        var plan = ActiveCommercePlan;
        if (plan is null)
        {
            return;
        }

        var index = plan.Nodes.FindIndex(node => node.Id == updatedNode.Id);
        if (index < 0)
        {
            return;
        }

        var existingNode = plan.Nodes[index];
        updatedNode.GeneratedImageIds = existingNode.GeneratedImageIds.ToList();
        updatedNode.SelectedImageId = existingNode.SelectedImageId;
        updatedNode.CompareImageId = existingNode.CompareImageId;
        updatedNode.Iterations = existingNode.Iterations.ToList();
        updatedNode.SelectedIterationId = existingNode.SelectedIterationId;
        updatedNode.VariantApplications = existingNode.VariantApplications.ToList();
        updatedNode.SelectedVariantApplicationId = existingNode.SelectedVariantApplicationId;
        updatedNode.LastGeneratedAt = existingNode.LastGeneratedAt;
        updatedNode.Normalize();
        updatedNode.Status = updatedNode.Enabled ? "已编辑" : "已暂停";
        plan.Nodes[index] = updatedNode;
        plan.UpdatedAt = DateTimeOffset.Now;
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task ApplyCommerceNodeToSession(CommerceImageNode node)
    {
        var product = ActiveCommerceProduct;
        if (product is null)
        {
            return;
        }

        node.Normalize();
        _commerceSelectedNodeId = node.Id;
        Settings.AspectRatio = node.AspectRatio;
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        _count = Math.Clamp(node.PlannedCount, 1, 8);
        ActiveSession.Prompt = BuildCommerceNodePrompt(node);
        _senderText = ActiveSession.Prompt;
        SyncCommerceProductReferencesToActiveSession(product);
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task GenerateCommerceNode(CommerceImageNode node)
    {
        await ApplyCommerceNodeToSession(node);
        if (!_loading)
        {
            _commerceGeneratingNodeId = node.Id;
            _commercePendingIteration = null;
            _commercePendingVariant = null;
            await GenerateAsync(ActiveSession.Prompt, addUserMessage: true, loadingLabel: $"正在生成{node.Title}");
        }
    }

    private async Task GenerateCommerceEnabledNodes()
    {
        var plan = ActiveCommercePlan;
        if (plan is null || _loading)
        {
            return;
        }

        foreach (var node in plan.Nodes.Where(node => node.Enabled).ToArray())
        {
            if (_cts?.IsCancellationRequested == true || !string.IsNullOrWhiteSpace(_error))
            {
                break;
            }

            await GenerateCommerceNode(node);
        }
    }

    private void AttachGeneratedImagesToCommerceNode(IReadOnlyList<GeneratedImage> images)
    {
        if (images.Count == 0 || string.IsNullOrWhiteSpace(_commerceGeneratingNodeId))
        {
            return;
        }

        var plan = ActiveCommercePlan;
        var node = plan?.Nodes.FirstOrDefault(node => node.Id == _commerceGeneratingNodeId);
        if (plan is null || node is null)
        {
            return;
        }

        node.GeneratedImageIds.AddRange(images.Select(image => image.Id));
        node.GeneratedImageIds = node.GeneratedImageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .TakeLast(96)
            .ToList();
        node.SelectedImageId = images.Last().Id;
        node.CompareImageId = node.CompareImageId is not null && node.GeneratedImageIds.Contains(node.CompareImageId, StringComparer.Ordinal)
            ? node.CompareImageId
            : node.GeneratedImageIds.FirstOrDefault(id => !string.Equals(id, node.SelectedImageId, StringComparison.Ordinal));
        node.LastGeneratedAt = DateTimeOffset.Now;
        node.Status = "已生成";
        AttachPendingCommerceIteration(node, images);
        AttachPendingCommerceVariant(node, images);
        plan.UpdatedAt = DateTimeOffset.Now;
        _commerceSelectedNodeId = node.Id;
        TouchCommerceWorkspace();
    }

    private void AttachPendingCommerceIteration(CommerceImageNode node, IReadOnlyList<GeneratedImage> images)
    {
        if (_commercePendingIteration is null ||
            !string.Equals(_commercePendingIteration.NodeId, node.Id, StringComparison.Ordinal))
        {
            return;
        }

        var resultIds = images
            .Select(image => image.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (resultIds.Count == 0)
        {
            return;
        }

        var iteration = new CommerceImageIteration
        {
            Name = _commercePendingIteration.Name,
            Mode = _commercePendingIteration.Mode,
            Label = _commercePendingIteration.Label,
            SourceImageId = _commercePendingIteration.SourceImageId,
            ResultImageIds = resultIds,
            SelectedImageId = resultIds.LastOrDefault(),
            Prompt = _commercePendingIteration.Prompt,
            CreatedAt = DateTimeOffset.Now,
        };
        iteration.Normalize(node.GeneratedImageIds);

        node.Iterations.Insert(0, iteration);
        node.SelectedIterationId = iteration.Id;
        node.SelectedImageId = iteration.SelectedImageId;
        node.CompareImageId = iteration.SourceImageId;
        node.Status = $"{iteration.Label}迭代";
        node.Normalize();
    }

    private void AttachPendingCommerceVariant(CommerceImageNode node, IReadOnlyList<GeneratedImage> images)
    {
        if (_commercePendingVariant is null ||
            !string.Equals(_commercePendingVariant.NodeId, node.Id, StringComparison.Ordinal))
        {
            return;
        }

        var resultIds = images
            .Select(image => image.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (resultIds.Count == 0)
        {
            return;
        }

        var variant = _commercePendingVariant.Variant;
        var application = new CommerceVariantApplication
        {
            VariantId = variant.Id,
            VariantName = variant.Name,
            Sku = variant.Sku,
            Color = variant.Color,
            Material = variant.Material,
            Size = variant.Size,
            Package = variant.Package,
            SourceImageId = _commercePendingVariant.SourceImageId,
            ResultImageIds = resultIds,
            SelectedImageId = resultIds.LastOrDefault(),
            Prompt = _commercePendingVariant.Prompt,
            Status = "已生成",
            CreatedAt = DateTimeOffset.Now,
        };
        application.Normalize(node.GeneratedImageIds);

        node.VariantApplications.RemoveAll(item =>
            string.Equals(item.VariantId, application.VariantId, StringComparison.Ordinal) &&
            string.Equals(item.SourceImageId, application.SourceImageId, StringComparison.Ordinal));
        node.VariantApplications.Insert(0, application);
        node.SelectedVariantApplicationId = application.Id;
        node.SelectedImageId = application.SelectedImageId;
        node.CompareImageId = application.SourceImageId;
        node.Status = "变体套用";
        node.Normalize();
        _commercePendingVariant = null;
    }

    private IReadOnlyList<GeneratedImage> GetCommerceNodeImages(CommerceImageNode? node)
    {
        if (node is null || node.GeneratedImageIds.Count == 0)
        {
            return [];
        }

        var ids = node.GeneratedImageIds.ToHashSet(StringComparer.Ordinal);
        return ActiveSession.Messages
            .SelectMany(message => message.Images)
            .Where(image => ids.Contains(image.Id))
            .OrderByDescending(image => image.CreatedAt)
            .ToArray();
    }

    private async Task SelectCommerceNodeImage(GeneratedImage image)
    {
        var node = ActiveCommerceNode;
        if (node is null)
        {
            return;
        }

        node.SelectedImageId = image.Id;
        node.SelectedIterationId = ResolveCommerceIterationForImage(node, image.Id)?.Id ?? node.SelectedIterationId;
        node.Status = image.IsFavorite ? "已选片" : "已生成";
        ActiveCommercePlan!.UpdatedAt = DateTimeOffset.Now;
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task SetCommerceCompareImage(GeneratedImage image)
    {
        var node = ActiveCommerceNode;
        if (node is null)
        {
            return;
        }

        node.CompareImageId = image.Id;
        node.Normalize();
        ActiveCommercePlan!.UpdatedAt = DateTimeOffset.Now;
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task RenameCommerceIteration(CommerceIterationRenameRequest request)
    {
        var node = ActiveCommerceNode;
        if (node is null)
        {
            return;
        }

        var iteration = node.Iterations.FirstOrDefault(item => string.Equals(item.Id, request.IterationId, StringComparison.Ordinal));
        if (iteration is null)
        {
            return;
        }

        iteration.Name = request.Name;
        node.Normalize();
        ActiveCommercePlan!.UpdatedAt = DateTimeOffset.Now;
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task SelectCommerceIteration(string iterationId)
    {
        var node = ActiveCommerceNode;
        if (node is null)
        {
            return;
        }

        var iteration = node.Iterations.FirstOrDefault(item => string.Equals(item.Id, iterationId, StringComparison.Ordinal));
        if (iteration is null)
        {
            return;
        }

        node.SelectedIterationId = iteration.Id;
        node.SelectedImageId = iteration.SelectedImageId ?? iteration.ResultImageIds.LastOrDefault() ?? node.SelectedImageId;
        node.CompareImageId = string.IsNullOrWhiteSpace(iteration.SourceImageId)
            ? node.CompareImageId
            : iteration.SourceImageId;
        node.Normalize();
        ActiveCommercePlan!.UpdatedAt = DateTimeOffset.Now;
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private async Task ToggleCommerceNodeImageFavorite(GeneratedImage image)
    {
        await ToggleFavorite(image);
        await SelectCommerceNodeImage(image);
        ActiveCommerceNode!.Status = image.IsFavorite ? "已选片" : "已生成";
        TouchCommerceWorkspace();
        await SaveAsync();
    }

    private void PreviewCommerceNodeImage(GeneratedImage image)
    {
        OpenImagePreview(image, ActiveCommerceNode?.Title ?? image.Prompt);
    }

    private Task UseCommerceNodeImageAsReference(GeneratedImage image) =>
        UseImageAsReference(image, image.ReferenceRole);

    private async Task GenerateCommerceCreativeIteration(CommerceIterationRequest request)
    {
        var product = ActiveCommerceProduct;
        var node = ActiveCommerceNode;
        var sourceImage = ResolveCommerceIterationSourceImage(node);
        if (product is null || node is null || sourceImage is null || _loading)
        {
            return;
        }

        var iteration = BuildCommerceIterationPrompt(request.Mode, product, node, sourceImage);
        var iterationName = NormalizeCommerceIterationName(request.Name, iteration.Label, node);
        ApplyImageSettings(sourceImage);
        Settings.AspectRatio = StudioSettings.NormalizeAspectRatio(node.AspectRatio);
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        _count = Math.Clamp(Math.Min(node.PlannedCount, 4), 1, 4);

        ClearAllReferenceInputs();
        ActiveSession.ImageReferences = sourceImage.Url;
        ActiveSession.MaskReference = string.Empty;
        ActiveSession.ReferenceRole = "content";
        ActiveSession.Prompt = iteration.Prompt;
        _senderText = iteration.Prompt;
        ApplyResolvedMode("image");
        _commerceGeneratingNodeId = node.Id;
        _commercePendingIteration = new PendingCommerceIteration(node.Id, iteration.Mode, iteration.Label, iterationName, sourceImage.Id, iteration.Prompt);
        node.Status = iteration.Status;
        ActiveCommercePlan!.UpdatedAt = DateTimeOffset.Now;
        TouchCommerceWorkspace();
        await SaveAsync();

        await GenerateAsync(iteration.Prompt, addUserMessage: true, loadingLabel: $"正在迭代{node.Title} · {iteration.Label}");
    }

    private Task GenerateCommerceCreativeIteration(string mode) =>
        GenerateCommerceCreativeIteration(new CommerceIterationRequest(mode, string.Empty));

    private async Task ApplyCommerceVariantsToSelectedMaster()
    {
        var product = ActiveCommerceProduct;
        var node = ActiveCommerceNode;
        var sourceImage = ResolveCommerceIterationSourceImage(node);
        if (product is null || node is null || sourceImage is null || product.SkuVariants.Count == 0 || _loading)
        {
            return;
        }

        var variants = product.SkuVariants
            .Where(variant => !IsEmptyCommerceVariant(variant))
            .Take(24)
            .ToArray();
        if (variants.Length == 0)
        {
            return;
        }

        ApplyImageSettings(sourceImage);
        Settings.AspectRatio = StudioSettings.NormalizeAspectRatio(node.AspectRatio);
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        _count = 1;
        _commerceAnalysisMessage = $"开始把母版套用到 {variants.Length} 个 SKU。";
        _commerceAnalysisIsError = false;

        foreach (var variant in variants)
        {
            if (_cts?.IsCancellationRequested == true || !string.IsNullOrWhiteSpace(_error))
            {
                break;
            }

            var prompt = BuildCommerceVariantPrompt(product, node, sourceImage, variant);
            ClearAllReferenceInputs();
            ActiveSession.ImageReferences = sourceImage.Url;
            ActiveSession.MaskReference = string.Empty;
            ActiveSession.ReferenceRole = "content";
            ActiveSession.Prompt = prompt;
            _senderText = prompt;
            ApplyResolvedMode("image");
            _commerceGeneratingNodeId = node.Id;
            _commercePendingIteration = null;
            _commercePendingVariant = new PendingCommerceVariant(node.Id, variant, sourceImage.Id, prompt);
            node.Status = "套用变体";
            ActiveCommercePlan!.UpdatedAt = DateTimeOffset.Now;
            TouchCommerceWorkspace();
            await SaveAsync();

            await GenerateAsync(prompt, addUserMessage: true, loadingLabel: $"正在套用{VariantLabel(variant)}");
        }

        if (string.IsNullOrWhiteSpace(_error))
        {
            _commerceAnalysisMessage = $"已完成 {node.VariantApplications.Count} 个变体归档。";
            _commerceAnalysisIsError = false;
            await SaveAsync();
        }
    }

    private GeneratedImage? ResolveCommerceIterationSourceImage(CommerceImageNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var images = GetCommerceNodeImages(node);
        return images.FirstOrDefault(image => string.Equals(image.Id, node.SelectedImageId, StringComparison.Ordinal))
            ?? images.FirstOrDefault();
    }

    private GeneratedImage? ResolveCommerceCompareImage(CommerceImageNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.CompareImageId))
        {
            return null;
        }

        return GetCommerceNodeImages(node)
            .FirstOrDefault(image => string.Equals(image.Id, node.CompareImageId, StringComparison.Ordinal));
    }

    private static CommerceImageIteration? ResolveCommerceIterationForImage(CommerceImageNode node, string imageId)
    {
        return node.Iterations.FirstOrDefault(iteration =>
            iteration.ResultImageIds.Contains(imageId, StringComparer.Ordinal));
    }

    private async Task ExportCommercePlan()
    {
        var product = ActiveCommerceProduct;
        var plan = ActiveCommercePlan;
        if (product is null || plan is null)
        {
            return;
        }

        var markdown = BuildCommercePlanMarkdown(product, plan);
        var bytes = Encoding.UTF8.GetBytes(markdown);
        var fileName = $"{SanitizeCommerceFileName(product.Name)}-image-plan-{DateTimeOffset.Now:yyyyMMdd-HHmm}.md";
        await JsRuntime.InvokeVoidAsync(
            "sonnetArt.downloadBytes",
            Convert.ToBase64String(bytes),
            fileName,
            "text/markdown;charset=utf-8");
        _downloadNotice = "图片规划书已导出。";
    }

    private async Task ExportCommercePackage()
    {
        var product = ActiveCommerceProduct;
        var plan = ActiveCommercePlan;
        if (product is null || plan is null)
        {
            return;
        }

        var request = new CommerceExportRequest(
            NormalizeCommerceExportPlatform(_commerceExportPlatform),
            NormalizeCommerceExportScope(_commerceExportScope),
            NormalizeCommerceExportImageSelection(_commerceExportImageSelection),
            NormalizeCommerceExportResolutionTier(_commerceExportResolutionTier),
            NormalizeCommerceExportPattern(_commerceExportFileNamePattern));
        var items = BuildCommerceExportItems(product, plan, request).ToList();
        if (items.Count == 0)
        {
            _downloadNotice = null;
            _error = "当前导出范围内没有可导出的图片。";
            return;
        }

        _loading = true;
        _loadingLabel = "正在打包导出";
        StateHasChanged();

        try
        {
            var manifest = new CommerceExportManifest
            {
                ProductId = product.Id,
                ProductName = product.Name,
                PlanId = plan.Id,
                PlanTitle = plan.Title,
                Platform = request.Platform,
                Scope = request.Scope,
                ImageSelection = request.ImageSelection,
                ResolutionTier = request.ResolutionTier,
                FileNamePattern = request.FileNamePattern,
                ExportedAt = DateTimeOffset.Now,
            };

            await using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    item.FilePath = MakeUniqueExportPath(item.FilePath, usedPaths);
                    var imageBytes = await TryReadExportImageBytesAsync(item.ImageUrl);
                    if (imageBytes is not null)
                    {
                        var entry = archive.CreateEntry(item.FilePath, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(imageBytes);
                        item.BinaryIncluded = true;
                    }

                    manifest.Items.Add(item);
                }

                manifest.ImageCount = manifest.Items.Count;
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, CommerceExportJsonOptions);
            }

            var fileName = $"{SanitizeCommerceFileName(product.Name)}-{request.Platform}-export-{DateTimeOffset.Now:yyyyMMdd-HHmm}.zip";
            await JsRuntime.InvokeVoidAsync(
                "sonnetArt.downloadBytes",
                Convert.ToBase64String(memory.ToArray()),
                fileName,
                "application/zip");
            _downloadNotice = $"已导出 {manifest.ImageCount} 条图片清单，其中 {manifest.Items.Count(item => item.BinaryIncluded)} 张图片已打包。";
            _error = null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or HttpRequestException or IOException or InvalidOperationException)
        {
            _downloadNotice = null;
            _error = $"导出失败：{TrimJsError(ex.Message)}";
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

        if (IsCommerceFallbackProductName(product.Name) && !string.IsNullOrWhiteSpace(analysis.ProductName))
        {
            product.Name = analysis.ProductName;
        }

        if (string.IsNullOrWhiteSpace(product.Description))
        {
            product.Description = !string.IsNullOrWhiteSpace(analysis.Summary)
                ? analysis.Summary
                : analysis.ProductType;
        }

        if (string.IsNullOrWhiteSpace(product.Specifications) && !string.IsNullOrWhiteSpace(analysis.Specifications))
        {
            product.Specifications = analysis.Specifications;
        }

        if (product.SellingPoints.Count == 0 && analysis.CoreSellingPoints.Count > 0)
        {
            product.SellingPoints = analysis.CoreSellingPoints.ToList();
        }

        if (string.IsNullOrWhiteSpace(product.TargetAudience) && analysis.TargetAudiences.Count > 0)
        {
            product.TargetAudience = string.Join("、", analysis.TargetAudiences.Take(3));
        }

        if (product.SkuVariants.Count == 0 && analysis.SkuVariants.Count > 0)
        {
            product.SkuVariants = analysis.SkuVariants.ToList();
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

    private static void ApplyCommerceGeneratedFields(CommerceProduct product)
    {
        if (string.IsNullOrWhiteSpace(product.Name))
        {
            product.Name = "图片识别商品";
        }

        if (string.IsNullOrWhiteSpace(product.Description) && product.ReferenceImages.Count > 0)
        {
            product.Description = "基于上传产品图创建的商品档案。";
        }

        product.Normalize();
    }

    private static string BuildCommerceFallbackProductName(IReadOnlyCollection<string> references)
    {
        return references.Count switch
        {
            <= 0 => "图片识别商品",
            1 => "图片识别商品",
            _ => $"图片识别商品 {references.Count} 张",
        };
    }

    private static bool IsCommerceFallbackProductName(string value)
    {
        var name = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, "图片识别商品", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("图片识别商品 ", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<CommerceExportManifestItem> BuildCommerceExportItems(
        CommerceProduct product,
        CommerceImagePlan plan,
        CommerceExportRequest request)
    {
        var nodes = SelectCommerceExportNodes(plan, request.Scope).ToArray();
        var platform = GetCommerceExportPlatformPreset(request.Platform);
        var imageIndex = 1;
        foreach (var node in nodes)
        {
            var nodeImages = GetCommerceNodeImages(node).ToDictionary(image => image.Id, StringComparer.Ordinal);
            foreach (var image in SelectCommerceExportImages(node, nodeImages, request.ImageSelection))
            {
                var variant = ResolveCommerceVariantApplicationForImage(node, image.Id);
                var item = CreateCommerceExportItem(product, node, variant, image, platform, request, imageIndex++);
                yield return item;
            }
        }
    }

    private IEnumerable<CommerceImageNode> SelectCommerceExportNodes(CommerceImagePlan plan, string scope)
    {
        return scope switch
        {
            "node" when ActiveCommerceNode is not null => [ActiveCommerceNode],
            "favorites" => plan.Nodes.Where(node => GetCommerceNodeImages(node).Any(image => image.IsFavorite)),
            _ => plan.Nodes,
        };
    }

    private static IEnumerable<GeneratedImage> SelectCommerceExportImages(
        CommerceImageNode node,
        IReadOnlyDictionary<string, GeneratedImage> nodeImages,
        string imageSelection)
    {
        if (nodeImages.Count == 0)
        {
            return [];
        }

        if (imageSelection == "all")
        {
            return nodeImages.Values.OrderBy(image => image.CreatedAt).ToArray();
        }

        var selectedIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(node.SelectedImageId))
        {
            selectedIds.Add(node.SelectedImageId);
        }

        selectedIds.AddRange(node.VariantApplications
            .Select(application => application.SelectedImageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))!);
        selectedIds.AddRange(node.Iterations
            .Select(iteration => iteration.SelectedImageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))!);

        var selected = selectedIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => nodeImages.TryGetValue(id, out var image) ? image : null)
            .Where(image => image is not null)
            .Cast<GeneratedImage>()
            .OrderBy(image => image.CreatedAt)
            .ToArray();

        if (selected.Length > 0 || imageSelection == "selected")
        {
            return selected;
        }

        return nodeImages.Values.OrderBy(image => image.CreatedAt).ToArray();
    }

    private CommerceExportManifestItem CreateCommerceExportItem(
        CommerceProduct product,
        CommerceImageNode node,
        CommerceVariantApplication? variant,
        GeneratedImage image,
        CommerceExportPlatformPreset platform,
        CommerceExportRequest request,
        int index)
    {
        var extension = ExtensionFromImage(image);
        var sku = variant?.Sku ?? string.Empty;
        var variantName = variant?.VariantName ?? string.Empty;
        var fallbackSku = string.IsNullOrWhiteSpace(sku)
            ? string.IsNullOrWhiteSpace(variantName) ? "base" : variantName
            : sku;
        var fileName = BuildCommerceExportFileName(request.FileNamePattern, product, node, variant, platform, index);
        var relativePath = CombineExportPath(
            platform.Id,
            SanitizeCommercePathSegment(product.Name),
            SanitizeCommercePathSegment(fallbackSku),
            SanitizeCommercePathSegment(node.Type),
            $"{fileName}.{extension}");

        return new CommerceExportManifestItem
        {
            FilePath = relativePath,
            ImageId = image.Id,
            ImageUrl = image.Url,
            ProductId = product.Id,
            ProductName = product.Name,
            NodeId = node.Id,
            NodeType = node.Type,
            NodeTitle = node.Title,
            VariantId = variant?.VariantId ?? string.Empty,
            VariantName = variant?.VariantName ?? string.Empty,
            Sku = sku,
            Color = variant?.Color ?? string.Empty,
            Material = variant?.Material ?? string.Empty,
            Size = variant?.Size ?? string.Empty,
            Package = variant?.Package ?? string.Empty,
            Platform = platform.Label,
            PresetSize = platform.Size,
            AspectRatio = string.IsNullOrWhiteSpace(image.AspectRatio) ? node.AspectRatio : image.AspectRatio,
            SourcePrompt = image.RequestPrompt,
            RequestSummary = image.RequestSummary,
            CreatedAt = image.CreatedAt,
        };
    }

    private static CommerceVariantApplication? ResolveCommerceVariantApplicationForImage(CommerceImageNode node, string imageId)
    {
        return node.VariantApplications.FirstOrDefault(application =>
            application.ResultImageIds.Contains(imageId, StringComparer.Ordinal));
    }

    private static string BuildCommerceExportFileName(
        string pattern,
        CommerceProduct product,
        CommerceImageNode node,
        CommerceVariantApplication? variant,
        CommerceExportPlatformPreset platform,
        int index)
    {
        var sku = variant?.Sku ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sku))
        {
            sku = string.IsNullOrWhiteSpace(variant?.VariantName) ? "base" : variant!.VariantName;
        }

        var value = pattern
            .Replace("{product}", product.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{sku}", sku, StringComparison.OrdinalIgnoreCase)
            .Replace("{variant}", variant?.VariantName ?? sku, StringComparison.OrdinalIgnoreCase)
            .Replace("{node}", node.Type, StringComparison.OrdinalIgnoreCase)
            .Replace("{nodeTitle}", node.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{platform}", platform.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", index.ToString("000"), StringComparison.OrdinalIgnoreCase);
        return SanitizeCommercePathSegment(value);
    }

    private async Task<byte[]?> TryReadExportImageBytesAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (TryReadDataUrlBytes(url, out var bytes))
        {
            return bytes;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            return await Http.GetByteArrayAsync(uri);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    private static bool TryReadDataUrlBytes(string url, out byte[] bytes)
    {
        bytes = [];
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = url.IndexOf(',');
        if (comma < 0 || comma == url.Length - 1)
        {
            return false;
        }

        var metadata = url[..comma];
        var payload = url[(comma + 1)..];
        try
        {
            bytes = metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string MakeUniqueExportPath(string path, HashSet<string> usedPaths)
    {
        if (usedPaths.Add(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidateName = $"{name}-{index:00}{extension}";
            var candidate = string.IsNullOrWhiteSpace(directory)
                ? candidateName
                : $"{directory}/{candidateName}";
            if (usedPaths.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string CombineExportPath(params string[] parts) =>
        string.Join("/", parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(SanitizeCommercePathSegment));

    private static string SanitizeCommercePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var cleaned = new string((value ?? string.Empty)
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray()).Trim();
        cleaned = string.Join("-", cleaned
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "item" : cleaned;
    }

    private static CommerceExportPlatformPreset GetCommerceExportPlatformPreset(string platform)
    {
        return NormalizeCommerceExportPlatform(platform) switch
        {
            "ozon" => new("ozon", "Ozon", "2000x2000", "1:1"),
            "mercado-libre" => new("mercado-libre", "Mercado Libre", "1200x1200", "1:1"),
            "shopee" => new("shopee", "Shopee", "1024x1024", "1:1"),
            "shopify" => new("shopify", "Shopify", "2048x2048", "1:1"),
            "independent" => new("independent", "独立站", "source", "auto"),
            _ => new("amazon", "Amazon", "2000x2000", "1:1"),
        };
    }

    private static string NormalizeCommerceExportPlatform(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ozon" => "ozon",
            "mercado-libre" => "mercado-libre",
            "shopee" => "shopee",
            "shopify" => "shopify",
            "independent" => "independent",
            _ => "amazon",
        };
    }

    private static string NormalizeCommerceExportScope(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "node" => "node",
            "favorites" => "favorites",
            _ => "plan",
        };
    }

    private static string NormalizeCommerceExportImageSelection(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "selected" => "selected",
            "all" => "all",
            _ => "selected-or-all",
        };
    }

    private static string NormalizeCommerceExportResolutionTier(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "1k" => "1k",
            "2k" => "2k",
            "4k" => "4k",
            "8mp" => "8mp",
            _ => "source",
        };
    }

    private static string NormalizeCommerceExportPattern(string? value)
    {
        var pattern = string.IsNullOrWhiteSpace(value) ? "{sku}-{node}-{index}" : value.Trim();
        return pattern.Length <= 120 ? pattern : pattern[..120].TrimEnd();
    }

    private void UpdateCommerceProductName(ChangeEventArgs args) => _commerceProductName = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductDescription(ChangeEventArgs args) => _commerceProductDescription = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductSellingPoints(ChangeEventArgs args) => _commerceProductSellingPoints = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductSpecifications(ChangeEventArgs args) => _commerceProductSpecifications = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductTargetAudience(ChangeEventArgs args) => _commerceProductTargetAudience = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceProductReferenceImages(ChangeEventArgs args) => _commerceProductReferenceImages = args.Value?.ToString() ?? string.Empty;
    private void UpdateCommerceSkuVariants(ChangeEventArgs args) => _commerceSkuVariants = args.Value?.ToString() ?? string.Empty;
    private async Task UpdateCommerceExportPlatform(ChangeEventArgs args)
    {
        _commerceExportPlatform = NormalizeCommerceExportPlatform(args.Value?.ToString());
        await SaveAsync();
    }

    private async Task UpdateCommerceExportScope(ChangeEventArgs args)
    {
        _commerceExportScope = NormalizeCommerceExportScope(args.Value?.ToString());
        await SaveAsync();
    }

    private async Task UpdateCommerceExportImageSelection(ChangeEventArgs args)
    {
        _commerceExportImageSelection = NormalizeCommerceExportImageSelection(args.Value?.ToString());
        await SaveAsync();
    }

    private async Task UpdateCommerceExportResolutionTier(ChangeEventArgs args)
    {
        _commerceExportResolutionTier = NormalizeCommerceExportResolutionTier(args.Value?.ToString());
        await SaveAsync();
    }

    private async Task UpdateCommerceExportFileNamePattern(ChangeEventArgs args)
    {
        _commerceExportFileNamePattern = NormalizeCommerceExportPattern(args.Value?.ToString());
        await SaveAsync();
    }

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

        if (_commerceEditingProductId is null && references.Count > 0)
        {
            await CreateCommerceProductFromUploadedReferences();
        }
    }

    private async Task CreateCommerceProductFromUploadedReferences()
    {
        if (_loading)
        {
            return;
        }

        var references = SplitCommerceReferenceImages(_commerceProductReferenceImages).ToList();
        if (references.Count == 0)
        {
            _commerceProductError = "请先上传至少一张商品图片。";
            return;
        }

        var product = new CommerceProduct
        {
            Name = BuildCommerceFallbackProductName(references),
            ReferenceImages = references,
            ReferenceRole = "product",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };
        product.Normalize();

        _commerceProductError = null;
        _commerceAnalysisMessage = null;
        _commerceAnalysisIsError = false;
        _loading = true;
        _loadingLabel = "正在识别商品";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        StateHasChanged();

        try
        {
            if (ChatReady)
            {
                var analysis = await ChatClient.AnalyzeCommerceProductAsync(Settings, product, _cts.Token);
                ApplyCommerceProductAnalysis(product, analysis);
                ApplyCommerceGeneratedFields(product);
                _commerceAnalysisMessage = "已根据产品图生成商品档案。";
            }
            else
            {
                ApplyCommerceGeneratedFields(product);
                _commerceAnalysisMessage = "已保存产品图。登录后可使用 AI 识别商品档案。";
            }

            product.UpdatedAt = DateTimeOffset.Now;
            product.Normalize();
            CommerceWorkspace.Products.Insert(0, product);
            CommerceWorkspace.ActiveProductId = product.Id;
            SyncCommerceProductReferencesToActiveSession(product);
            TouchCommerceWorkspace();
            _commerceProductDialogOpen = false;
            _commerceProductError = null;
            await SaveAsync();
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            _commerceProductError = "本次商品识别已取消。";
        }
        catch (Exception ex)
        {
            _commerceProductError = ex.Message;
        }
        finally
        {
            _loading = false;
            _loadingLabel = "正在请求图像接口";
            StateHasChanged();
        }
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
                Material = parts.Length > 3 ? parts[2] : string.Empty,
                Size = parts.Length > 4 ? parts[3] : string.Empty,
                Package = parts.Length > 5 ? parts[4] : string.Empty,
                Sku = parts.Length switch
                {
                    3 => parts[2],
                    > 5 => parts[5],
                    _ => string.Empty,
                },
            };
        }
    }

    private static string FormatCommerceSkuVariant(CommerceSkuVariant variant)
    {
        var parts = new[] { variant.Name, variant.Color, variant.Material, variant.Size, variant.Package, variant.Sku }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" | ", parts);
    }

    private static bool IsEmptyCommerceVariant(CommerceSkuVariant variant) =>
        string.IsNullOrWhiteSpace(variant.Name) &&
        string.IsNullOrWhiteSpace(variant.Color) &&
        string.IsNullOrWhiteSpace(variant.Material) &&
        string.IsNullOrWhiteSpace(variant.Size) &&
        string.IsNullOrWhiteSpace(variant.Package) &&
        string.IsNullOrWhiteSpace(variant.Sku);

    private static string VariantLabel(CommerceSkuVariant variant)
    {
        if (!string.IsNullOrWhiteSpace(variant.Sku))
        {
            return variant.Sku;
        }

        if (!string.IsNullOrWhiteSpace(variant.Name))
        {
            return variant.Name;
        }

        return BuildCommerceVariantFacts(variant);
    }

    private static string BuildCommerceVariantFacts(CommerceSkuVariant variant)
    {
        var parts = new List<string>();
        AppendCommerceVariantFact(parts, "名称", variant.Name);
        AppendCommerceVariantFact(parts, "颜色", variant.Color);
        AppendCommerceVariantFact(parts, "材质", variant.Material);
        AppendCommerceVariantFact(parts, "尺寸", variant.Size);
        AppendCommerceVariantFact(parts, "套装", variant.Package);
        AppendCommerceVariantFact(parts, "SKU", variant.Sku);
        return parts.Count == 0 ? "未命名 SKU" : string.Join("；", parts);
    }

    private static void AppendCommerceVariantFact(List<string> parts, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}：{value.Trim()}");
        }
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
            Scene = ExtractCommerceNodeScene(instruction),
            Composition = instruction,
            KeyMessage = goal,
            Status = "待确认",
            Prompt = $"{facts}{Environment.NewLine}{Environment.NewLine}图片任务：{instruction}",
            NegativePrompt = negativePrompt,
            ReferenceRole = "product",
            Enabled = true,
        };
    }

    private static string ExtractCommerceNodeScene(string instruction)
    {
        var comma = instruction.IndexOf('，');
        return comma > 0 ? instruction[..comma] : instruction;
    }

    private static string BuildCommerceNodePrompt(CommerceImageNode node)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(node.Prompt))
        {
            parts.Add(node.Prompt);
        }

        if (!string.IsNullOrWhiteSpace(node.Scene))
        {
            parts.Add($"场景：{node.Scene}");
        }

        if (!string.IsNullOrWhiteSpace(node.Composition))
        {
            parts.Add($"构图：{node.Composition}");
        }

        if (!string.IsNullOrWhiteSpace(node.KeyMessage))
        {
            parts.Add($"核心表达：{node.KeyMessage}");
        }

        if (!string.IsNullOrWhiteSpace(node.NegativePrompt))
        {
            parts.Add($"避免：{node.NegativePrompt}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static CommerceIterationPrompt BuildCommerceIterationPrompt(
        string mode,
        CommerceProduct product,
        CommerceImageNode node,
        GeneratedImage sourceImage)
    {
        var sourcePrompt = !string.IsNullOrWhiteSpace(sourceImage.RequestPrompt)
            ? sourceImage.RequestPrompt
            : sourceImage.Prompt;
        var basePrompt = !string.IsNullOrWhiteSpace(node.Prompt)
            ? node.Prompt
            : sourcePrompt;
        var normalizedMode = mode?.Trim().ToLowerInvariant();
        var (iterationMode, label, focus, status) = normalizedMode switch
        {
            "texture" => ("texture", "质感", "强化产品材质、纹理、触感、边缘质感和工艺细节，保持商品结构、颜色和比例一致。", "质感迭代"),
            "style" => ("style", "风格", "在不改变商品真实性的前提下，探索更高级的电商视觉风格、背景调性和品牌感，保持可上架。", "风格迭代"),
            "detail" => ("detail", "详情", "把画面转化为详情页表达，突出卖点、使用利益和局部信息层级，文字少且清晰，不编造品牌或参数。", "详情迭代"),
            _ => ("lighting", "光影", "优化布光、阴影、反光和空间层次，让商品更立体、更干净、更适合首轮选片。", "光影迭代"),
        };

        var prompt = string.Join(Environment.NewLine + Environment.NewLine, new[]
        {
            $"基于参考图为「{product.Name}」生成{node.Title}的{label}迭代版本。",
            $"商品事实：{BuildCommerceProductFacts(product)}",
            $"原节点目标：{node.Goal}",
            $"迭代方向：{focus}",
            $"原始提示词：{basePrompt}",
            "保留产品主体、结构、颜色关系和关键卖点，不改变 SKU、不添加虚假 logo、认证、价格、功效或活动信息。",
            string.IsNullOrWhiteSpace(node.NegativePrompt) ? string.Empty : $"避免：{node.NegativePrompt}",
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return new CommerceIterationPrompt(iterationMode, label, prompt, status);
    }

    private static string BuildCommerceVariantPrompt(
        CommerceProduct product,
        CommerceImageNode node,
        GeneratedImage sourceImage,
        CommerceSkuVariant variant)
    {
        var sourcePrompt = !string.IsNullOrWhiteSpace(sourceImage.RequestPrompt)
            ? sourceImage.RequestPrompt
            : sourceImage.Prompt;
        var basePrompt = !string.IsNullOrWhiteSpace(node.Prompt)
            ? node.Prompt
            : sourcePrompt;
        var variantFacts = BuildCommerceVariantFacts(variant);

        return string.Join(Environment.NewLine + Environment.NewLine, new[]
        {
            $"基于参考母版图，为「{product.Name}」生成{node.Title}的 SKU 变体图。",
            $"套用目标：{variantFacts}",
            $"商品事实：{BuildCommerceProductFacts(product)}",
            $"母版节点目标：{node.Goal}",
            $"母版提示词：{basePrompt}",
            "严格复用参考母版的构图、镜头角度、商品姿态、光影关系、背景层级、节点类型和画面信息结构。",
            "只把商品主体或套装内容替换为目标 SKU 对应的颜色、材质、尺寸和套装配置；如果某个维度为空，则沿用母版。",
            "保持产品结构、比例、品牌识别和关键卖点一致，不重新规划整套图片，不添加虚假 logo、认证、价格、功效或活动信息。",
            string.IsNullOrWhiteSpace(node.NegativePrompt) ? string.Empty : $"避免：{node.NegativePrompt}",
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string NormalizeCommerceIterationName(string? value, string label, CommerceImageNode node)
    {
        var name = value?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Length <= 48 ? name : name[..48].TrimEnd();
        }

        var next = node.Iterations.Count(iteration => string.Equals(iteration.Label, label, StringComparison.OrdinalIgnoreCase)) + 1;
        return $"{label}版本 {next}";
    }

    private string BuildCommercePlanMarkdown(CommerceProduct product, CommerceImagePlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {plan.Title}");
        builder.AppendLine();
        builder.AppendLine($"- 商品：{product.Name}");
        builder.AppendLine($"- 方案模型：{(string.IsNullOrWhiteSpace(plan.Model) ? "本地规划" : plan.Model)}");
        builder.AppendLine($"- 更新时间：{plan.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        builder.AppendLine($"- 节点：{plan.Nodes.Count}");
        builder.AppendLine($"- 已归档图片：{plan.Nodes.Sum(node => node.GeneratedImageIds.Count)}");
        if (!string.IsNullOrWhiteSpace(plan.StrategySummary))
        {
            builder.AppendLine($"- 策略：{plan.StrategySummary}");
        }

        builder.AppendLine();
        builder.AppendLine("## 商品档案");
        AppendCommerceMarkdownLine(builder, "描述", product.Description);
        AppendCommerceMarkdownList(builder, "核心卖点", product.SellingPoints);
        AppendCommerceMarkdownLine(builder, "规格", product.Specifications);
        AppendCommerceMarkdownLine(builder, "目标人群", product.TargetAudience);
        AppendCommerceMarkdownList(builder, "SKU", product.SkuVariants.Select(FormatCommerceSkuVariant));

        if (product.Analysis.HasContent)
        {
            builder.AppendLine();
            builder.AppendLine("## AI 商品分析");
            AppendCommerceMarkdownLine(builder, "产品类型", product.Analysis.ProductType);
            AppendCommerceMarkdownLine(builder, "摘要", product.Analysis.Summary);
            AppendCommerceMarkdownList(builder, "AI 卖点", product.Analysis.CoreSellingPoints);
            AppendCommerceMarkdownList(builder, "适用场景", product.Analysis.UseScenarios);
            AppendCommerceMarkdownList(builder, "颜色变体", product.Analysis.ColorVariants);
            AppendCommerceMarkdownList(builder, "材质特性", product.Analysis.MaterialFeatures);
            AppendCommerceMarkdownList(builder, "目标人群", product.Analysis.TargetAudiences);
        }

        builder.AppendLine();
        builder.AppendLine("## 图片规划节点");
        foreach (var node in plan.Nodes)
        {
            node.Normalize();
            var images = GetCommerceNodeImages(node);
            builder.AppendLine();
            builder.AppendLine($"### {node.Title}");
            builder.AppendLine();
            builder.AppendLine($"- 类型：{node.Type}");
            builder.AppendLine($"- 状态：{node.Status}");
            builder.AppendLine($"- 启用：{(node.Enabled ? "是" : "否")}");
            builder.AppendLine($"- 比例：{node.AspectRatio}");
            builder.AppendLine($"- 计划数量：{node.PlannedCount}");
            builder.AppendLine($"- 已生成：{images.Count}");
            builder.AppendLine($"- 迭代版本：{node.Iterations.Count}");
            if (node.LastGeneratedAt is not null)
            {
                builder.AppendLine($"- 最近生成：{node.LastGeneratedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
            }

            AppendCommerceMarkdownLine(builder, "目标", node.Goal);
            AppendCommerceMarkdownLine(builder, "场景", node.Scene);
            AppendCommerceMarkdownLine(builder, "构图", node.Composition);
            AppendCommerceMarkdownLine(builder, "核心信息", node.KeyMessage);
            builder.AppendLine();
            builder.AppendLine("提示词：");
            builder.AppendLine("```text");
            builder.AppendLine(node.Prompt);
            builder.AppendLine("```");
            if (!string.IsNullOrWhiteSpace(node.NegativePrompt))
            {
                builder.AppendLine();
                builder.AppendLine("负向提示词：");
                builder.AppendLine("```text");
                builder.AppendLine(node.NegativePrompt);
                builder.AppendLine("```");
            }

            if (images.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("图片结果：");
                foreach (var image in images.OrderBy(image => image.CreatedAt))
                {
                    var selected = string.Equals(node.SelectedImageId, image.Id, StringComparison.Ordinal) ? " · 选片" : string.Empty;
                    var compare = string.Equals(node.CompareImageId, image.Id, StringComparison.Ordinal) ? " · 对比基准" : string.Empty;
                    builder.AppendLine($"- {image.Id}{selected}{compare} · {image.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {image.Size} · {image.RequestSummary}");
                }
            }

            if (node.Iterations.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("迭代版本：");
                foreach (var iteration in node.Iterations.OrderBy(iteration => iteration.CreatedAt))
                {
                    builder.AppendLine($"- {iteration.Name} · {iteration.Label} · {iteration.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 来源 {iteration.SourceImageId} · 结果 {string.Join(", ", iteration.ResultImageIds)}");
                }
            }

            if (node.VariantApplications.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("SKU 变体套用：");
                foreach (var variant in node.VariantApplications.OrderBy(variant => variant.CreatedAt))
                {
                    var variantFacts = string.Join("；", new[]
                    {
                        string.IsNullOrWhiteSpace(variant.VariantName) ? string.Empty : $"名称：{variant.VariantName}",
                        string.IsNullOrWhiteSpace(variant.Color) ? string.Empty : $"颜色：{variant.Color}",
                        string.IsNullOrWhiteSpace(variant.Material) ? string.Empty : $"材质：{variant.Material}",
                        string.IsNullOrWhiteSpace(variant.Size) ? string.Empty : $"尺寸：{variant.Size}",
                        string.IsNullOrWhiteSpace(variant.Package) ? string.Empty : $"套装：{variant.Package}",
                        string.IsNullOrWhiteSpace(variant.Sku) ? string.Empty : $"SKU：{variant.Sku}",
                    }.Where(part => !string.IsNullOrWhiteSpace(part)));
                    builder.AppendLine($"- {variantFacts} · {variant.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 母版 {variant.SourceImageId} · 结果 {string.Join(", ", variant.ResultImageIds)}");
                }
            }
        }

        return builder.ToString();
    }

    private static void AppendCommerceMarkdownLine(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {label}：{value.Trim()}");
        }
    }

    private static void AppendCommerceMarkdownList(StringBuilder builder, string label, IEnumerable<string> values)
    {
        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.AppendLine($"- {label}：{string.Join("；", items)}");
    }

    private static string SanitizeCommerceFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string((value ?? string.Empty)
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "commerce-product" : cleaned;
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

    private sealed record CommerceIterationPrompt(string Mode, string Label, string Prompt, string Status);

    private sealed record CommerceExportPlatformPreset(
        string Id,
        string Label,
        string Size,
        string AspectRatio);

    private sealed record PendingCommerceIteration(
        string NodeId,
        string Mode,
        string Label,
        string Name,
        string SourceImageId,
        string Prompt);

    private sealed record PendingCommerceVariant(
        string NodeId,
        CommerceSkuVariant Variant,
        string SourceImageId,
        string Prompt);
}
