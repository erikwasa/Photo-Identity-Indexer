# Photo Identity - Azure caption GPU state

 

# Azure resources

$Rg         = "rg-photoidentity-captions"

$Location   = "swedencentral"

$Env        = "pi-caption-gpu-env"

$GpuProfile = "gpu-t4"

 

# Ollama Container App

$OllamaApp  = "pi-ollama-t4"

$OllamaFqdn = "pi-ollama-t4.blackmeadow-6a858a97.swedencentral.azurecontainerapps.io"

$AzureOllamaUrl  = "https://pi-ollama-t4.blackmeadow-6a858a97.swedencentral.azurecontainerapps.io"

 

# Container registry

$AcrName        = "picaption937031"

$AcrLoginServer = "picaption937031.azurecr.io"

$OllamaImage    = "picaption937031.azurecr.io/pi-ollama:investigation"

 

# Persistent model storage

$StorageAccount = "picaption311569"

$FileShare      = "ollama-models"

$StorageMount   = "ollamamodels"

$VolumeName     = "ollama-models-volume"

$ModelPath      = "/models"

 

# Model provenance - important for Photo Identity

$ModelName = "qwen2.5vl:3b"

$ExpectedDigest = "fb90415cde1ef08aa669ae74b082d49b158729b6db1ab183c941417d507e71a1"

 

# Ollama / Photo Identity settings

$OllamaContextTokens = 1024

$CaptionImageMode    = "thumbnail-480x320"

 

# Current management PC public IP used by ingress ACL

$ManagementPublicIp = "90.143.31.85"