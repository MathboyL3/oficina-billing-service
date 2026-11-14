# oficina-billing-service

Orçamento e pagamento (Mercado Pago) da OS — Saga sobre RabbitMQ (MassTransit) com MongoDB.

## Configuração
- `ConnectionStrings:Mongo` — exige replica set (transações + outbox). Local: `mongodb://localhost:27017/?replicaSet=rs0&directConnection=true`.
- `RabbitMq:*` — Host/Port/VirtualHost/Username/Password (montados campo a campo, nunca por URI).
- `Jwt:*` — mesmos Issuer/Audience/Secret do monólito (o serviço só valida o token).
- `MercadoPago:Modo` — `Fake` (padrão) ou `Real`.
- **Segredos:** em produção `Jwt__Secret`, `MercadoPago__AccessToken` e `MercadoPago__WebhookSecret` devem vir de variável de
  ambiente / Secret do Kubernetes. O `Jwt:Secret` de `appsettings.json` é só o segredo de desenvolvimento do monólito (já público naquele repositório).

## Garantias
- Outbox transacional Mongo: a escrita e as mensagens publicadas confirmam juntas; o inbox descarta reentregas.
- Índice único por OS em `orcamentos` e `pagamentos`; duplicidade vira 409 na API e é tratada como idempotente nos consumidores.
- Campo `Versao` nos agregados: a gravação é otimista (`ReplaceOne` por `Id` + `Versao`); perdeu a corrida = já processado.
- Webhook `POST /webhooks/mercadopago`: assinatura inválida/ausente/`ts` malformado = 401 sem tocar em nada. Com `data.id` na query
  (formato do MP) a assinatura é validada antes de ler o corpo; o corpo só é lido antes quando o `data.id` existe apenas nele.
- `external_reference` da preferência do MP = `OrdemServicoId` e `X-Idempotency-Key` = `pref-{OrdemServicoId}`: a retomada de um
  pagamento sem preferência não duplica a cobrança e o webhook resolve o pagamento por OS.
- Consumidores não capturam `DuplicidadeException`: o E11000 aborta a transação Mongo, então a exceção propaga e o retry do
  MassTransit reprocessa a mensagem pelo caminho idempotente.
- `GET /api/orcamentos/{id}`, `GET /api/orcamentos?osId=` e `GET /api/pagamentos/{id}`: cliente só lê o que é seu (403); staff lê tudo.

## Runbook: estorno falho após cancelamento
Pagamento aprovado depois do cancelamento da OS é estornado automaticamente (log `pagamento_apos_cancelamento`). Se o estorno falhar, o pagamento fica `EstornoPendente` e é publicado `EstornoFalhou`. Sem Saga para reprocessar:
1. Consultar o painel do Mercado Pago (o pagamento já foi estornado lá? o MP é a fonte da verdade).
2. Chamar `POST /api/pagamentos/{id}/reprocessar-estorno` (role `Gerente`): consulta o MP antes e, se já estiver `refunded`, apenas marca `Estornado` (sem chamar o estorno; se a consulta falhar, estorna mesmo assim). Em `Estornado`/outro status devolve 409; se falhar de novo continua `EstornoPendente`.

**Risco residual:** se o commit se perder após um estorno real no MP, o pagamento fica Pendente até a próxima notificação do MP (efunded), que reconcilia (log estorno_reconciliado, publica PagamentoEstornado) quando a OS está cancelada/recusada.

## Testes e cobertura
`dotnet test Billing.slnx` exige >= 80% de linhas e **precisa de Docker**: `MongoPersistenciaTests` sobe um MongoDB 7 em replica set
(Testcontainers) e exercita repositórios, índices únicos, concorrência otimista e o unit of work (commit/rollback) de verdade.

Exclusões de cobertura restantes:
- `Program.cs`, `**/DependencyInjection.cs`, `**/Migrations/*.cs`, `**/Persistence/Configurations/*.cs`, `**/*DbContextFactory.cs` (`ExcludeByFile` no csproj de testes; wiring/composição).
- `MessagingExtensions` (`[ExcludeFromCodeCoverage]`): só configura MassTransit/RabbitMQ/outbox; o fluxo mensagem -> consumidor -> outbox não é exercitado nos testes.
## Rodando localmente
Requer Docker. Os 3 serviços compartilham a rede externa `oficina-dev` (os containers se enxergam pelo nome):
1. Uma vez: `docker network create oficina-dev`
2. Suba o **os-service primeiro** (traz o RabbitMQ): `docker compose up -d --build` em `oficina-os-service`
3. Depois `oficina-execucao-service` e `oficina-billing-service` (mesmo comando).

APIs: os `:5001`, billing `:5002`, execucao `:5003` (`/health/ready`). RabbitMQ management: `:15672` (guest/guest).

## Deploy no Kubernetes

Pré-requisitos:
- `fiap-infra-k8s` aplicado (namespace `oficina` e RabbitMQ com o Secret `rabbitmq-credentials`).
- Variáveis de ambiente: `JWT_SECRET`, `MP_ACCESS_TOKEN`, `MP_WEBHOOK_SECRET`, `NEW_RELIC_LICENSE_KEY`, `GHCR_USER` e `GHCR_TOKEN` (PAT com `read:packages`).

Passos (a partir da raiz do repo):
1. `./k8s/bootstrap-secrets.ps1` — cria/atualiza os Secrets e o `ghcr-pull` de forma idempotente (falha se uma env obrigatória estiver vazia). `k8s/secret.example.yaml` documenta as chaves.
2. `kubectl apply -k k8s/`

**Notas:** o Mongo (`billing-mongo`) roda **sem autenticação** e só é acessível dentro do cluster. Se o PVC for recriado, reexecute o Job de init do replica set: `kubectl delete job billing-mongo-init -n oficina && kubectl apply -k k8s/`. `MercadoPago__PublicBaseUrl` aponta para o Kong local (`http://localhost:32080/billing`); a rota `/billing` no Kong é criada na Task 4.4.
