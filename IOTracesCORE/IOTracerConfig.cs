namespace IOTracesCORE
{
    internal class IOTracerConfig
    {
        public string OutputPath { get; set; }
        public bool Anonymous { get; set; }
        public bool R2UploadEnabled { get; set; }
        public string R2AccountId { get; set; }
        public string R2BucketName { get; set; }
    }
}