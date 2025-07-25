namespace EinAutomation.Api.Options
{
    public class AzureBlobStorageOptions
    {
        public string ConnectionString { get; set; } = null!;
        public string Container { get; set; } = null!;
    }
}