Demo application to show waiting for multiple approvals in Durable Functions with timeout.

### Submit an approval request

```powershell
$approvalRequest = "{ approverCount: 5, requiredApprovals: 3, timeoutMinutes: 2 }"
$starterUri = "http://localhost:7071/api/RequestApproval"
$statusUris = Invoke-RestMethod -Method Post -Body $approvalRequest -Uri $starterUri
```

### Request approval status

```powershell
Invoke-RestMethod -Method Get -Uri $statusUris.statusQueryGetUri
```

###  Submit Approval

```powershell
$submitUri = "http://localhost:7071/api/SubmitApproval/$($statusUris.id)"
Invoke-RestMethod -Method Post -Uri $submitUri -Body '{ approved: true, approver: "approver 1" }'
```

### Flows to test:

[x] Timeout
[x] Approved
[x] Same approver submits many
[x] Race condition - should be fixed in [v1.8.0](https://github.com/Azure/azure-functions-durable-extension/releases/tag/v1.8.0)
[x] Approval rejected (also allows same user to send a reject after initally approving)