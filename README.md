# oficina-billing-service

Orçamento e pagamento (Mercado Pago) da OS — Saga sobre RabbitMQ (MassTransit) com MongoDB.

## Configuração
- `ConnectionStrings:Mongo` — exige replica set (transações + outbox). Local: `mongodb://localhost:27017/?replicaSet=rs0&directConnection=true`.
- `RabbitMq:*` — Host/Port/VirtualHost/Username/Password (montados campo a campo, nunca por URI).
- `Jwt:*` — mesmos Issuer/Audience/Secret do monólito (o serviço só valida o token).
- `MercadoPago:Modo` — `Fake` (padrão) ou `Real`. `AccessToken` e `WebhookSecret` vêm de variável de ambiente
  (`MercadoPago__AccessToken`, `MercadoPago__WebhookSecret`); nada de segredo real no repositório.

## Garantias
- Outbox transacional Mongo: a escrita e as mensagens publicadas confirmam juntas; o inbox descarta reentregas.
- Índice único por OS em `orcamentos` e `pagamentos`; duplicidade vira 409 na API e é tratada como idempotente nos consumidores.
- Campo `Versao` nos agregados: a gravação é otimista (`ReplaceOne` por `Id` + `Versao`); perdeu a corrida = já processado.
- Webhook `POST /webhooks/mercadopago`: assinatura inválida/ausente/`ts` malformado = 401 sem tocar em nada.

## Testes e cobertura
`dotnet test Billing.slnx` exige >= 80% de linhas. `MongoContext`, repositórios Mongo, `MongoSessao`, `MongoUnitOfWork`
e `MongoMapeamentos` têm `[ExcludeFromCodeCoverage]`: o CI de testes não tem MongoDB. Eles são cobertos por
`MongoRealSmokeTests`, que só executa com `BILLING_MONGO_TEST` apontando para um Mongo em replica set
(ex.: `docker run -d -p 27099:27017 mongo:7 --replSet rs0` + `rs.initiate`).