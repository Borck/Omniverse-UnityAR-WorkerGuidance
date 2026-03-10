namespace Guidance.Runtime
{
    public sealed class StepActivationDto
    {
        public string JobId { get; }
        public string StepId { get; }
        public string PartId { get; }
        public string DisplayName { get; }

        public StepActivationDto(string jobId, string stepId, string partId, string displayName)
        {
            JobId = jobId;
            StepId = stepId;
            PartId = partId;
            DisplayName = displayName;
        }
    }
}
