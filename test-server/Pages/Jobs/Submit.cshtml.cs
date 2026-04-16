using System.Text.Json;
using GuidanceAdminServer.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GuidanceAdminServer.Pages.Jobs;

public sealed class SubmitModel : PageModel
{
    private readonly JobStore _jobStore;
    private readonly FileAssetStore _assetStore;

    public SubmitModel(JobStore jobStore, FileAssetStore assetStore)
    {
        _jobStore = jobStore;
        _assetStore = assetStore;
    }

    [BindProperty] public string JobId { get; set; } = string.Empty;
    [BindProperty] public string WorkflowVersion { get; set; } = string.Empty;
    [BindProperty] public List<StepInput> Steps { get; set; } = [];
    [BindProperty] public List<IFormFile?> GlbFiles { get; set; } = [];
    [BindProperty] public List<IFormFile?> TargetFiles { get; set; } = [];

    public string ErrorMessage { get; private set; } = string.Empty;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(JobId) || Steps.Count == 0)
        {
            ErrorMessage = "Please fill in all required fields and add at least one step.";
            return Page();
        }

        var stepRecords = new List<StepRecord>();

        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            var glb = i < GlbFiles.Count ? GlbFiles[i] : null;
            var target = i < TargetFiles.Count ? TargetFiles[i] : null;

            var glbFileName = glb?.FileName ?? string.Empty;
            var targetFileName = target?.FileName ?? string.Empty;

            if (glb != null && glb.Length > 0)
            {
                await _assetStore.SaveGlbAsync(glb, step.AssetVersion);
            }

            if (target != null && target.Length > 0)
            {
                await _assetStore.SaveTargetAsync(target, step.TargetVersion ?? string.Empty);
            }

            var safetyNotes = (step.SafetyNotes ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            stepRecords.Add(new StepRecord(
                StepId: step.StepId,
                PartId: step.PartId,
                DisplayName: step.DisplayName,
                InstructionsShort: step.InstructionsShort ?? string.Empty,
                SafetyNotes: safetyNotes,
                AssetVersion: step.AssetVersion,
                TargetId: step.TargetId ?? string.Empty,
                TargetVersion: step.TargetVersion ?? string.Empty,
                AnchorType: step.AnchorType ?? string.Empty,
                AnimationName: step.AnimationName ?? string.Empty,
                GlbFileName: glbFileName,
                TargetFileName: targetFileName
            ));
        }

        var job = new JobRecord(JobId, WorkflowVersion, stepRecords);
        _jobStore.Upsert(job);

        // Write manifest JSON compatible with the Python server's manifest format
        var manifest = new
        {
            jobId = JobId,
            workflowVersion = WorkflowVersion,
            steps = stepRecords.Select(s => new
            {
                stepId = s.StepId,
                partId = s.PartId,
                assetVersion = s.AssetVersion,
                glbFile = s.GlbFileName,
                stepJsonFile = string.Empty,
                targetVersion = s.TargetVersion,
                targetFile = s.TargetFileName,
                compression = "NONE",
            }),
        };
        await _assetStore.SaveManifestAsync(JobId, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        // Activate the job and push Step 1 to all connected Unity clients
        _jobStore.SetActiveJob(JobId);

        TempData["Message"] = $"Job '{JobId}' submitted and Unity devices notified.";
        return RedirectToPage("/Index");
    }

    public sealed class StepInput
    {
        public string StepId { get; set; } = string.Empty;
        public string PartId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? InstructionsShort { get; set; }
        public string? SafetyNotes { get; set; }
        public string AssetVersion { get; set; } = string.Empty;
        public string? TargetId { get; set; }
        public string? TargetVersion { get; set; }
        public string? AnchorType { get; set; }
        public string? AnimationName { get; set; }
    }
}
