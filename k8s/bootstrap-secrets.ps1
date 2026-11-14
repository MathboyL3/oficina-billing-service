# Cria/atualiza (idempotente) os Secrets do servico no namespace oficina a partir de variaveis de ambiente.
# Nenhum valor fica neste arquivo. Uso: definir as envs abaixo e rodar .\k8s\bootstrap-secrets.ps1
$ErrorActionPreference = 'Stop'
$ns = 'oficina'

function Exigir([string[]]$nomes) {
    $faltando = $nomes | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
    if ($faltando) { throw "Variaveis de ambiente obrigatorias vazias: $($faltando -join ', ')" }
}
function Env([string]$nome) { [Environment]::GetEnvironmentVariable($nome) }

function Aplicar([string[]]$criar) {
    $yaml = & kubectl @criar --namespace $ns --dry-run=client -o yaml
    if ($LASTEXITCODE -ne 0) { throw "kubectl create falhou: $($criar -join ' ')" }
    $yaml | kubectl apply -f -
    if ($LASTEXITCODE -ne 0) { throw 'kubectl apply falhou' }
}
Exigir @('JWT_SECRET','MP_ACCESS_TOKEN','MP_WEBHOOK_SECRET','NEW_RELIC_LICENSE_KEY','GHCR_USER','GHCR_TOKEN')

Aplicar @('create','secret','generic','oficina-billing-service-secrets',
    '--from-literal=ConnectionStrings__Mongo=mongodb://billing-mongo-0.billing-mongo.oficina.svc.cluster.local:27017/?replicaSet=rs0&directConnection=true',
    "--from-literal=Jwt__Secret=$(Env 'JWT_SECRET')",
    "--from-literal=MercadoPago__AccessToken=$(Env 'MP_ACCESS_TOKEN')",
    "--from-literal=MercadoPago__WebhookSecret=$(Env 'MP_WEBHOOK_SECRET')",
    "--from-literal=NEW_RELIC_LICENSE_KEY=$(Env 'NEW_RELIC_LICENSE_KEY')")

Aplicar @('create','secret','docker-registry','ghcr-pull',
    '--docker-server=ghcr.io',
    "--docker-username=$(Env 'GHCR_USER')",
    "--docker-password=$(Env 'GHCR_TOKEN')")