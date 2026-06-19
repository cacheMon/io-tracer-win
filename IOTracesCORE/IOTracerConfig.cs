namespace IOTracesCORE
{
    internal class IOTracerConfig
    {
        public string OutputPath { get; set; } = string.Empty;
        public bool Anonymous { get; set; }
        public bool R2UploadEnabled { get; set; }
        public string R2AccountId { get; set; } = string.Empty;
        public string R2BucketName { get; set; } = string.Empty;
    }
}