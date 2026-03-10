namespace Guidance.Runtime
{
    public sealed class StepActivationDto
    {
        public string JobId { get; }
        public string StepId { get; }
        public string PartId { get; }
        public string DisplayName { get; }
        public string AssetVersion { get; }
        public string TargetId { get; }
        public string TargetVersion { get; }

        public StepActivationDto(
            string jobId,
            string stepId,
            string partId,
            string displayName,
            string assetVersion = "",
            string targetId = "",
            string targetVersion = "")
        {
            JobId = jobId;
            StepId = stepId;
            PartId = partId;
            DisplayName = displayName;
            AssetVersion = assetVersion;
            TargetId = targetId;
            TargetVersion = targetVersion;
        }
    }
}
