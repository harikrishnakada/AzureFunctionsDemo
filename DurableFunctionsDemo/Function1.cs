using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Threading;
using Dynamitey.DynamicObjects;
using System.Collections.Generic;

namespace DurableFunctionsDemo
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            string instanceId = await starter.StartNewAsync("OrchestratatorFunction", data);

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("OrchestratatorFunction")]
        public static async Task<object> OrchestratatorFunction(
           [OrchestrationTrigger] IDurableOrchestrationContext context,
           ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var outputs = new List<object>();
            // Get the input object
            var SyncObj = context.GetInput<object>();

            var deadline = context.CurrentUtcDateTime.AddMinutes(1);

            var ct = new CancellationTokenSource();
            var timeOut = context.CreateTimer(deadline, ct.Token);

            var externalEvent = context.WaitForExternalEvent("WaitingForApproval");

            var winner = await Task.WhenAny(timeOut, externalEvent);

            if (winner == externalEvent)
            {
                var result = await context.CallActivityAsync<string>("AF_APPROLVAL", "Approved");
                outputs.Add(result);
            }
            else
            {
                var result = await context.CallActivityAsync<string>("AF_EXCALATION", "HOD");
                outputs.Add(result);
            }

            if (!timeOut.IsCompleted)
            {
                // All pending timers must be complete or canceled before the function exits.
                ct.Cancel();
            }

            ct.Dispose();

            return outputs;

        }

        [FunctionName("AF_APPROLVAL")]
        public static async Task<object> AF_APPROLVAL([ActivityTrigger] object SyncObj, ILogger log)
        {
            return $"The request is : {JsonConvert.SerializeObject(SyncObj)}";
        }

        [FunctionName("AF_EXCALATION")]
        public static async Task<object> AF_EXCALATION([ActivityTrigger] object SyncObj, ILogger log)
        {
            return $"The request is excalted to: {JsonConvert.SerializeObject(SyncObj)}";
        }

        [FunctionName("AF_WAITINGFORAPPROVAL")]
        public static async Task AF_WAITINGFORAPPROVAL(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "raiseEvent/{instanceId}")] HttpRequest req, string instanceId, [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var noFilter = new OrchestrationStatusQueryCondition();
            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                noFilter,
                CancellationToken.None);

            foreach (DurableOrchestrationStatus instance in result.DurableOrchestrationState)
            {
                log.LogInformation(JsonConvert.SerializeObject(instance));
            }

            await client.RaiseEventAsync(instanceId, "WaitingForApproval", true);
        }

        [FunctionName("AF_GETINSTANCES")]
        public static async Task<IActionResult> AF_GETINSTANCES(
           [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "instances/{instanceId}")] HttpRequest req, string instanceId, [DurableClient] IDurableOrchestrationClient client,
           ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var noFilter = new OrchestrationStatusQueryCondition();
            OrchestrationStatusQueryResult result = await client.ListInstancesAsync(
                noFilter,
                CancellationToken.None);

            foreach (DurableOrchestrationStatus instance in result.DurableOrchestrationState)
            {
                log.LogInformation(JsonConvert.SerializeObject(instance.InstanceId));
            }

            return new OkObjectResult(result.DurableOrchestrationState);

        }
    }
}
