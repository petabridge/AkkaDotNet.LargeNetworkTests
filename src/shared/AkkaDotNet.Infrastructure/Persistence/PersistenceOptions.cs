namespace AkkaDotNet.Infrastructure.Persistence
{
    public class PersistenceOptions
    {
        public bool Enabled { get; set; } = false;

        public string AzureStorageConnectionString { get; set; } = string.Empty;
    }
}
