namespace SonnetArt.Services;

public static class ImageEditPromptBuilder
{
    public static string BuildMaskedRevisionPrompt(string instruction, string previousPrompt)
    {
        var trimmedInstruction = instruction.Trim();
        var priorPromptSection = string.IsNullOrWhiteSpace(previousPrompt)
            ? string.Empty
            : $"""

            上一轮提示词：{previousPrompt.Trim()}
            """;

        return $"""
            这是一次局部标注修图，不是重新生成整张图片。输入图片是编辑目标图，不是仅供参考的风格图。

            遮罩/标注区域是唯一允许修改的范围。只修改用户标注的遮罩区域内的内容；未标注区域必须保持原样，尽可能保持像素级一致。

            用户要修改：{trimmedInstruction}
            {priorPromptSection}

            编辑要求：标注/遮罩只用于定位，最终图片中不要保留任何标注痕迹。修改区域要和周围像素自然衔接，边缘干净，光影、材质、透视、清晰度、肤色和色彩与原图一致。

            保留约束：不要改变未标注区域；不要重新构图；不要重绘整张图；不要改变人物身份、脸部、发型、表情、身体姿势、服装、背景、灯光、镜头角度、画幅比例、照片风格或画质。

            如果修改目标是手部，请修正为自然、正常、符合人体结构的手：手指数量正确，关节清楚，手掌比例合理，手腕与手臂连接自然，手位符合原姿势和透视。

            负面约束：换人，换脸，换衣服，换背景，改变姿势，改变构图，新增人物，新增多余物体，畸形手，多指，少指，断指，粘连手指，扭曲手掌，错误关节，手腕断裂，模糊手部，脸部变形，身体比例错误，过度修饰，低质量，失真。
            """;
    }

    public static string BuildMaskedRevisionUserMessage(string instruction)
    {
        var trimmedInstruction = instruction.Trim();
        return $"""
            局部标注修图：{trimmedInstruction}

            只修改已标注/遮罩区域。未标注区域必须保持原样；不要重新生成整张图，不要改变人物、服装、背景、姿势、构图或风格。标注只用于定位，最终图中不要保留标注。
            """;
    }

    public static string BuildOutpaintPrompt(string direction, string previousPrompt)
    {
        var normalizedDirection = NormalizeDirection(direction);
        var directionInstruction = normalizedDirection switch
        {
            "left" => "向画面左侧扩展并平移视角，补全左侧新画布。",
            "right" => "向画面右侧扩展并平移视角，补全右侧新画布。",
            "up" => "向画面上方扩展并平移视角，补全天空、背景或上方空间。",
            "down" => "向画面下方扩展并平移视角，补全地面、前景或下方空间。",
            "continue" => "沿画面主要运动、视线或叙事方向继续延展，生成自然衔接的后续空间。",
            _ => "向四周扩展画布，补全边缘之外的自然内容。",
        };
        var priorPromptSection = string.IsNullOrWhiteSpace(previousPrompt)
            ? string.Empty
            : $"""

            原始提示词：{previousPrompt.Trim()}
            """;

        return $"""
            这是一次 GPT Image 2 图片编辑/扩图请求。输入图片是需要延展的目标图。

            任务：{directionInstruction}
            {priorPromptSection}

            生成要求：保留原图已有内容、主体身份、产品外观、人物特征、构图重心、镜头语言、透视、光影、材质、色彩和画质；新增区域必须与原图自然连续，边缘干净，没有拼接痕迹。

            扩展要求：不要裁切或缩小原图主体，不要改变已有主体，不要改变原图风格，不要新增无关文字、Logo、水印或边框。新增区域应像原图本来就存在的一部分。

            负面约束：变形主体，改变人物身份，改变产品结构，换背景，重绘整张图，重复主体，拼贴感，边缘断裂，视角冲突，透视错误，模糊，低质量，水印，文字噪声。
            """;
    }

    public static string BuildOutpaintUserMessage(string direction, string targetSize)
    {
        var action = NormalizeDirection(direction) switch
        {
            "left" => "向左平移扩图",
            "right" => "向右平移扩图",
            "up" => "向上平移扩图",
            "down" => "向下平移扩图",
            "continue" => "续画",
            _ => "一键扩图",
        };
        var size = string.IsNullOrWhiteSpace(targetSize) ? "当前尺寸" : targetSize.Trim();

        return $"""
            {action}：保持原图主体和风格一致，补全自然连续的新画布。目标尺寸：{size}。
            """;
    }

    private static string NormalizeDirection(string? direction)
    {
        return direction?.Trim().ToLowerInvariant() switch
        {
            "left" => "left",
            "right" => "right",
            "up" => "up",
            "down" => "down",
            "continue" => "continue",
            _ => "expand",
        };
    }
}
