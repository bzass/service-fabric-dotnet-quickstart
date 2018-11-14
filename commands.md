## Connect to cluster:

```
Connect-ServiceFabricCluster -ConnectionEndpoint mstraining2.westeurope.cloudapp.azure.com:19000 -X509Credential -ServerCertThumbprint <certThumbprintFromKV> -FindType FindByThumbprint -FindValue <certThumbprintFromKV> -StoreLocation CurrentUser -StoreName My
```

## Deploy new instance of an app

```
New-ServiceFabricApplication -ApplicationName fabric:/Voting2 -ApplicationTypeName "VotingType" -ApplicationTypeVersion "1.0.0" -ApplicationParameter @{VotingWeb_PortNo='8081'} 
```
