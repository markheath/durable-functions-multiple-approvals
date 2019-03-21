using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading;

namespace DurableFunctionsMultipleApprovals
{
    public static class Function1
    {
        [FunctionName("RequestApproval")]
        public static async Task<IActionResult> RequestApproval(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            [OrchestrationClient] DurableOrchestrationClientBase client, ILogger log)
        {
            log.LogInformation("Requesting a new approval orchestration.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var approvalConfig = JsonConvert.DeserializeObject<ApprovalConfig>(requestBody);
            if (approvalConfig.ApproverCount <= 0 || approvalConfig.TimeoutMinutes <= 0
                || approvalConfig.RequiredApprovals <= 0)
                return new BadRequestObjectResult("Invalid Approval Config");

            var orchestrationId = await client.StartNewAsync(nameof(GetApprovalOrchestrator), approvalConfig);

            return new OkObjectResult(client.CreateHttpManagementPayload(orchestrationId));
        }

        [FunctionName("SubmitApproval")]
        public static async Task<IActionResult> SubmitApproval(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "SubmitApproval/{id}")] HttpRequest req,
            [OrchestrationClient] DurableOrchestrationClientBase client, string id, ILogger log)
        {
            log.LogInformation("Passing on an approval result.");

            
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var approvalResult = JsonConvert.DeserializeObject<ApprovalResult>(requestBody);
            if (string.IsNullOrEmpty(approvalResult.Approver))
                return new BadRequestObjectResult("Invalid Approval Result");
            if (string.IsNullOrEmpty(id))
                return new BadRequestObjectResult("Invalid Orchestration id");

            await client.RaiseEventAsync(id, ApprovalResultEventName, approvalResult);

            var status = await client.GetStatusAsync(id, false, false);
            return new OkObjectResult(status);
        }
        const string ApprovalResultEventName = "ApprovalResult";
        [FunctionName(nameof(GetApprovalOrchestrator))]
        public static async Task<string> GetApprovalOrchestrator([OrchestrationTrigger]
            DurableOrchestrationContextBase ctx, ILogger log)
        {
            var approvalConfig = ctx.GetInput<ApprovalConfig>();
            string result;
            var expireAt = ctx.CurrentUtcDateTime.AddMinutes(approvalConfig.TimeoutMinutes);
            for(var n = 0; n < approvalConfig.ApproverCount; n++)
            {
                // todo: send a message to each approver
                if (!ctx.IsReplaying) log.LogInformation($"Requesting approval from Approver {n + 1}");
            }

            var cts = new CancellationTokenSource();
            var timerTask = ctx.CreateTimer(expireAt, cts.Token);

            var approvers = new HashSet<string>();
            while(true)
            {
                var externalEventTask = ctx.WaitForExternalEvent<ApprovalResult>(ApprovalResultEventName);
                var completed = await Task.WhenAny(timerTask,externalEventTask);
                if (completed == timerTask)
                {
                    result = $"Timed out with {approvers.Count} approvals so far";
                    if (!ctx.IsReplaying) log.LogWarning(result);
                    break; // end orchestration
                }
                else if (completed == externalEventTask)
                {
                    var approver = externalEventTask.Result.Approver;
                    if (externalEventTask.Result.Approved)
                    {
                        approvers.Add(approver);
                        if (!ctx.IsReplaying) log.LogInformation($"Approval received from {approver}");
                        if (approvers.Count >= approvalConfig.RequiredApprovals)
                        {
                            result = $"Approved ({approvers.Count} approvals received)";
                            if (!ctx.IsReplaying) log.LogInformation(result);
                            break;
                        }
                    }
                    else
                    {
                        result = $"Rejected by {approver}";
                        if (!ctx.IsReplaying) log.LogWarning(result);
                        break;
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unexpected result from Task.WhenAny");
                }
            }
            cts.Cancel();
            return result;
        }

    }

    class ApprovalConfig
    {
        public int ApproverCount { get; set; }
        public int RequiredApprovals { get; set; }
        public int TimeoutMinutes { get; set; }
    }

    class ApprovalResult
    {
        public bool Approved { get; set; }
        public string Approver { get; set; }
    }

}
