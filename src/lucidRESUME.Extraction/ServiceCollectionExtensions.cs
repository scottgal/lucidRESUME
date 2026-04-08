using lucidRESUME.Core.Interfaces;
using lucidRESUME.Extraction.Ner;
using lucidRESUME.Extraction.Pipeline;
using lucidRESUME.Extraction.Recognizers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucidRESUME.Extraction;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExtraction(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IEntityDetector, ResumeRecognizerDetector>();

        // General NER (dslim/bert-base-NER): PER, ORG, LOC, MISC - high accuracy for names
        var generalNerSection = config.GetSection("GeneralNer");
        var generalNerOpts = new OnnxNerOptions
        {
            ModelPath = generalNerSection["ModelPath"] ?? "models/ner/model.onnx",
            VocabPath = generalNerSection["VocabPath"] ?? "models/ner/vocab.txt",
            ConfidenceThreshold = generalNerSection.GetValue<double?>("ConfidenceThreshold") ?? 0.80,
            MaxSequenceLength = generalNerSection.GetValue<int?>("MaxSequenceLength") ?? 512,
            Labels = OnnxNerOptions.GeneralNerLabels,
            LowerCase = false, // bert-base-NER is a cased model
        };
        services.AddSingleton<IEntityDetector>(sp =>
            new OnnxNerDetector(generalNerOpts, sp.GetRequiredService<ILoggerFactory>().CreateLogger<OnnxNerDetector>()));

        // Resume NER (yashpwr/resume-ner-bert-v2): Skills, Degree, JobTitle, etc.
        // This is a CASED model (bert-base-cased) — LowerCase must be false.
        var resumeNerSection = config.GetSection("OnnxNer");
        var resumeNerOpts = new OnnxNerOptions();
        resumeNerSection.Bind(resumeNerOpts);
        resumeNerOpts.LowerCase = false; // bert-base-cased — do NOT lowercase input
        if (string.IsNullOrEmpty(resumeNerOpts.ModelPath))
            resumeNerOpts.ModelPath = "models/resume-ner/model.onnx";
        if (string.IsNullOrEmpty(resumeNerOpts.VocabPath))
            resumeNerOpts.VocabPath = "models/resume-ner/vocab.txt";
        resumeNerOpts.Labels ??= OnnxNerOptions.ResumeNerLabels;
        services.AddSingleton<IEntityDetector>(sp =>
            new OnnxNerDetector(resumeNerOpts, sp.GetRequiredService<ILoggerFactory>().CreateLogger<OnnxNerDetector>()));

        services.AddSingleton<ExtractionPipeline>();
        return services;
    }
}