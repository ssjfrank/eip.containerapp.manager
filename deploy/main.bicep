param location string = resourceGroup().location
param environmentName string
param logAnalyticsName string
param appInsightsName string
param storageAccountName string
param keyVaultName string
param communicationName string
param containerAppName string
param image string
param registryServer string
param registryUsername string
@secure()
param registryPassword string

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  sku: { name: 'PerGB2018' }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
}

resource kv 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: { name: 'standard' family: 'A' }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
  }
}

resource comm 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationName
  location: location
  properties: {
    dataLocation: 'UnitedStates'
  }
}

resource env 'Microsoft.App/managedEnvironments@2024-02-02' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-02-02' = {
  name: containerAppName
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: registryServer
          username: registryUsername
          passwordSecretRef: 'regpwd'
        }
      ]
      secrets: [
        {
          name: 'regpwd'
          value: registryPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'manager'
          image: image
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
          ]
          probes: [
            {
              type: 'liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
            }
            {
              type: 'readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
            }
          ]
        }
      ]
      scale: {
        minReplicas: 2
        maxReplicas: 2
      }
    }
  }
}


