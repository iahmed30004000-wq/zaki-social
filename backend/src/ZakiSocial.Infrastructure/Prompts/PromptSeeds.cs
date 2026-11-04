namespace ZakiSocial.Infrastructure.Prompts;

public sealed record PromptSeed(string Name, string Category, string Task, string? PreferredAi, string Body);

/// <summary>
/// المكتبة الجاهزة: بتنزرع بقاعدة البيانات لكل مساحة جديدة (مرة وحدة) وبعدها المالك والمحرر بعدّلوها من الواجهة.
/// هي مجرد نقطة بداية؛ ما في أي برومبت مكتوب بالكود وقت التشغيل.
/// </summary>
public static class PromptSeeds
{
    public static readonly IReadOnlyList<PromptSeed> All =
    [
        new("caption_facebook", "content", "caption", "claude",
            "اكتب منشور فيسبوك جذاب بالعربية عن {{topic}} موجّه لجمهور {{audience}}.\nالنبرة: {{tone}}.\nأضف دعوة واضحة لاتخاذ إجراء: {{cta}}.\nلا تستعمل أكثر من 3 إيموجي. أعد نص المنشور فقط بدون أي شرح."),
        new("caption_instagram", "content", "caption", "claude",
            "اكتب كابشن إنستغرام قصير ومؤثر بالعربية عن {{topic}} لجمهور {{audience}} بنبرة {{tone}}.\nاختم بـ 5 هاشتاغات مناسبة. أعد الكابشن فقط."),
        new("ideas_campaign", "content", "ideas", "claude",
            "اقترح {{count}} أفكار لمنشورات أو حملة عن {{topic}} لجمهور {{audience}}.\nلكل فكرة: عنوان من سطر واحد وجملة تشرح الزاوية. رقّم الأفكار."),
        new("rewrite_tone", "content", "rewrite", "claude",
            "أعد صياغة النص التالي بنبرة {{tone}} مع الحفاظ على المعنى والمعلومات كما هي:\n\n{{text}}\n\nأعد النص الجديد فقط."),
        new("hashtags", "content", "hashtags", "local",
            "اقترح {{count}} هاشتاغات مناسبة للنص التالي، مفصولة بمسافة فقط ودون أي شرح:\n\n{{text}}"),
        new("reply_comment", "reply", "reply", "local",
            "اكتب رداً قصيراً ولطيفاً (جملتين على الأكثر) على هذا التعليق: «{{comment}}»\nمعلومات عن المتجر: {{context}}\nأعد الرد فقط."),
        new("classify_comment", "reply", "classify", "local",
            "صنّف التعليق التالي إلى واحد فقط من: سؤال سعر، شكوى، مديح، مزعج، أخرى.\nالتعليق: {{comment}}\nأعد التصنيف فقط."),
        new("analysis_performance", "analysis", "analysis", "claude",
            "هذه أرقام أداء المنشورات:\n{{metrics}}\n\nحلّل الأرقام واقترح 3 إجراءات عملية محددة: ماذا ننشر أكثر، ماذا نوقف، ومتى. اكتب بالعربية وباختصار."),
        new("best_time", "analysis", "analysis", "claude",
            "من بيانات الأداء التالية:\n{{metrics}}\n\nاقترح أفضل 3 أوقات للنشر بتوقيت {{timezone}} مع سبب مختصر لكل وقت."),
    ];
}
