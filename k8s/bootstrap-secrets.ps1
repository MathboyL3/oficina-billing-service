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
Exigir @('JWT_SECRET','MP_ACCESS_TOKEN','MP_WEBHOOK_SECRET','NEW_RELIC_LICENSE_KEY')

$billing = @('create','secret','generic','oficina-billing-service-secrets',
    '--from-literal=ConnectionStrings__Mongo=mongodb://billing-mongo-0.billing-mongo.oficina.svc.cluster.local:27017/?replicaSet=rs0&directConnection=true',
    "--from-literal=Jwt__Secret=$(Env 'JWT_SECRET')",
    "--from-literal=MercadoPago__AccessToken=$(Env 'MP_ACCESS_TOKEN')",
    "--from-literal=MercadoPago__WebhookSecret=$(Env 'MP_WEBHOOK_SECRET')",
    "--from-literal=NEW_RELIC_LICENSE_KEY=$(Env 'NEW_RELIC_LICENSE_KEY')")

# Opcionais, por ambiente: o Secret entra depois do ConfigMap no envFrom, entao estes valores sobrepoem o ConfigMap
# (que fica com Modo=Fake como padrao) e nao sao desfeitos pelo deploy do pipeline.
$porAmbiente = [ordered]@{
    MP_MODO            = 'MercadoPago__Modo'
    MP_SANDBOX         = 'MercadoPago__Sandbox'
    MP_PUBLIC_BASE_URL = 'MercadoPago__PublicBaseUrl'
}
foreach ($nome in $porAmbiente.Keys) {
    if (-not [string]::IsNullOrWhiteSpace((Env $nome))) { $billing += "--from-literal=$($porAmbiente[$nome])=$(Env $nome)" }
}
Aplicar $billing

# So necessario se os pacotes do GHCR forem privados (hoje sao publicos).
if (-not [string]::IsNullOrWhiteSpace((Env 'GHCR_TOKEN'))) {
    Aplicar @('create','secret','docker-registry','ghcr-pull',
        '--docker-server=ghcr.io',
        "--docker-username=$(Env 'GHCR_USER')",
        "--docker-password=$(Env 'GHCR_TOKEN')")
}