using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Newtonsoft.Json;
using StatefulService = Microsoft.ServiceFabric.Services.Runtime.StatefulService;

namespace ReportGenerator
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class ReportGenerator : StatefulService
    {
        public ReportGenerator(StatefulServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new ServiceReplicaListener[]
            {
                new ServiceReplicaListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatefulServiceContext>(serviceContext)
                                            .AddSingleton<IReliableStateManager>(this.StateManager))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // break cancellation token support
            // cancellationToken = CancellationToken.None;

            var fabricClient = new FabricClient();
            var serviceName = GetVotingDataServiceName(Context);
            var httpClient = new HttpClient();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var reverseProxyBaseUri = new Uri(Context.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings.Sections["ReverseProxy"].Parameters["BaseUri"].Value);
                var proxyAddress = new Uri($"{reverseProxyBaseUri}{serviceName.AbsolutePath}");
                var partitions = await fabricClient.QueryManager.GetPartitionListAsync(serviceName, null, TimeSpan.FromSeconds(10), cancellationToken);

                var requests = partitions.Select(async p =>
                {
                    var proxyUrl = $"{proxyAddress}/api/VoteData?PartitionKey={((Int64RangePartitionInformation) p.PartitionInformation).LowKey}&PartitionKind=Int64Range";

                    var response = await httpClient.GetAsync(proxyUrl, cancellationToken);
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        return new List<KeyValuePair<string, int>>();
                    }

                    return await response.Content.ReadAsAsync<List<KeyValuePair<string, int>>>(cancellationToken);
                });

                var results = await Task.WhenAll(requests);
                var numberOfVotes = results.SelectMany(l => l).Sum(kv => kv.Value);

                var reportDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>(ReportsDictionaryName);

                using (var tx = this.StateManager.CreateTransaction())
                {
                    await reportDictionary.AddOrUpdateAsync(tx, NumberOfvotesEntryName, 1, (key, oldvalue) => numberOfVotes);
                    await tx.CommitAsync();
                }

                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
        }

        /// <summary>
        /// Constructs a service name for a specific poll.
        /// Example: fabric:/VotingApplication/polls/name-of-poll
        /// </summary>
        /// <param name="poll"></param>
        /// <returns></returns>
        internal static Uri GetVotingDataServiceName(ServiceContext context)
        {
            return new Uri($"{context.CodePackageActivationContext.ApplicationName}/VotingData");
        }

        internal static string ReportsDictionaryName => "reports";
        internal static string NumberOfvotesEntryName => "NumberOfvotes";

    }
}
