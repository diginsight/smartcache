namespace ABB.EL.Common.Commons.Helpers
{
    public sealed class ParallelServiceOptions : IParallelServiceOptions
    {
        public int LowConcurrency { get; set; }
        public int MediumConcurrency { get; set; }
        public int HighConcurrency { get; set; }
    }
}
