# API do laboratório

O JSON OpenAPI gerado pela aplicação fica em `/openapi/v1.json`. A API e o painel são servidos na mesma origem quando o Compose está em execução.

| Operação | Rota | Acesso |
| --- | --- | --- |
| Listar viagens | `GET /api/v1/shipments` | Operador |
| Consultar medições e avaliação | `GET /api/v1/shipments/{id}` | Operador da organização |
| Ingerir uma leitura | `POST /api/v1/shipments/{id}/readings` | Sensor vinculado à viagem |
| Registrar revisão | `POST /api/v1/shipments/{id}/reviews` | Operador da organização |

A credencial local vai em `Authorization: Bearer <chave>`. A viagem e a organização autorizadas vêm da credencial, não do corpo da requisição. As chaves de laboratório não devem ser usadas em um serviço publicado.

## Leituras

```json
{
  "id": "11e84e67-fe9e-4e67-980e-9516db84a9d2",
  "observedAt": "2026-11-26T12:05:00Z",
  "celsius": 12.0
}
```

`202` significa que a leitura foi persistida ou que a mesma leitura já tinha sido aceita. O sensor deve manter o mesmo identificador nos reenvios. O mesmo ID com outro conteúdo, ou IDs distintos com o mesmo instante normalizado, causam `409`. A API exige offset explícito em `observedAt`, guarda `receivedAt` e não presume que as leituras chegam em ordem.

## Revisões

```json
{
  "expectedVersion": 4,
  "decision": "acknowledged",
  "note": "Desvio confirmado após análise do histórico."
}
```

Use a `evaluatedVersion` da avaliação que foi apresentada ao operador. A revisão aceita retorna `200` com o detalhe atualizado. Se há leituras pendentes, se a versão mudou, se não há desvio confirmado ou se a versão já foi revisada, o servidor responde `409`. A revisão registra a ciência do desvio, não a liberação da carga.

## Estados

- `pending`: ainda não existe uma avaliação ou há leituras novas que o worker não avaliou.
- `insufficientEvidence`: as amostras não bastam para concluir que a série permaneceu na faixa, nem confirmam um desvio sustentado.
- `withinRange`: a série observada preenche os critérios da demo e está dentro da faixa. Não representa certificação da carga.
- `excursion`: a série sustenta um desvio segundo a regra configurada no laboratório.

Na avaliação, `excursion.detectedAt` é o horário **observado** da amostra que completou a duração exigida. Não é o horário em que o worker recebeu a mensagem.

Sem credencial válida, as rotas protegidas devolvem `401`. Um papel incorreto ou um sensor fora da viagem devolve `403`. Recurso não encontrado no escopo do operador devolve `404`. Formato inválido ou campos não suportados no corpo devolvem `400`.
