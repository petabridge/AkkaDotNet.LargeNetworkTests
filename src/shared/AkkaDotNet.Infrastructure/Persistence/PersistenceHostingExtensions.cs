using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Azure;

namespace AkkaDotNet.Infrastructure.Persistence;

public static class PersistenceHostingExtensions
{
    public static Config GetPersistenceHocon(string connectionString)
    {
        return $@"
            akka.persistence {{
                journal {{
                    azure-table {{
                        class = ""Akka.Persistence.Azure.Journal.AzureTableStorageJournal, Akka.Persistence.Azure""
                        connection-string = ""{connectionString}""
                    }}
                }}
                 snapshot-store {{
                     azure-blob-store {{
                        class = ""Akka.Persistence.Azure.Snapshot.AzureBlobSnapshotStore, Akka.Persistence.Azure""
                        connection-string = ""{connectionString}""
                    }}
                }}
            }}";
    }
    
    public static AkkaConfigurationBuilder AddPersistence(this AkkaConfigurationBuilder configurationBuilder,
        PersistenceOptions options)
    {
        if (options.Enabled)
        {
            var persistenceHocon = GetPersistenceHocon(options.AzureStorageConnectionString)
                .WithFallback(AzurePersistence.DefaultConfig);
            configurationBuilder.AddHocon(persistenceHocon);
        }

        return configurationBuilder;
    }
}