# SinalFrio · Painel de operações

O painel Angular 22.3 inspeciona remessas da cadeia fria e permite registrar ciência de uma excursão confirmada. O navegador chama `/api/v1` na mesma origem; sirva o diretório `dist/sinalfrio/browser` com o proxy reverso do projeto. Não há dados de demonstração nem fallback local. Cole a chave do operador na tela; ela é enviada como token Bearer e não é persistida.

## Verificação

Com Node 24.15+ na linha 24 (verificado em 24.22.3):

```sh
npm ci
npm test
npm run build
```

Caso a versão local seja incompatível, no diretório `frontend/` execute:

```sh
docker run --rm --user "$(id -u):$(id -g)" -e npm_config_cache=/tmp/npm-cache \
  -v "$PWD:/app" -w /app node:24-bookworm-slim sh -ec 'npm ci; npm test; npm run build'
```

Os testes validam a distinção entre revisão atual e anterior e a elegibilidade para revisar uma excursão. Em operação, o painel consulta a lista e a inspeção selecionada a cada quatro segundos; um conflito no registro dispara atualização imediata e preserva a justificativa.
