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

## Testes e cobertura
`dotnet test Billing.slnx` exige >= 80% de linhas e **precisa de Docker**: `MongoPersistenciaTests` sobe um MongoDB 7 em replica set
(Testcontainers) e exercita repositórios, índices únicos, concorrência otimista e o unit of work (commit/rollback) de verdade.

Exclusões de cobertura restantes:
- `Program.cs`, `**/DependencyInjection.cs`, `**/Migrations/*.cs`, `**/Persistence/Configurations/*.cs`, `**/*DbContextFactory.cs` (`ExcludeByFile` no csproj de testes; wiring/composição).
- `MessagingExtensions` (`[ExcludeFromCodeCoverage]`): só configura MassTransit/RabbitMQ/outbox; o fluxo mensagem -> consumidor -> outbox não é exercitado nos testes.
