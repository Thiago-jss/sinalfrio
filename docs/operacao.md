# Operação do laboratório

## Passos

```sh
python3 scripts/bootstrap.py
docker compose up -d --build --wait
curl --fail http://127.0.0.1:5186/health/ready
```

`bootstrap.py` cria um `.env` privado e preserva as credenciais existentes. Em ambientes já aprovisionados, não troque a senha no arquivo esperando que o PostgreSQL ou o RabbitMQ reconfigurem o volume. A inicialização das contas só acontece na primeira criação do volume. Use dados artificiais e não compartilhe o `.env`.

O Compose aplica migrations e prepara o cenário com a unidade `migrate`. O processo HTTP não aplica alterações de esquema ao iniciar. Por design, RabbitMQ e PostgreSQL ficam na rede interna do projeto. `docker compose stop` preserva dados; `docker compose down --volumes` remove os volumes **do projeto atual** e só deve ser usado em laboratórios descartáveis.

## Diagnóstico

| Sinal | Ação |
| --- | --- |
| Aceitou uma leitura, mas o painel ainda mostra versão anterior | Verifique se `readingVersion > evaluatedVersion`, o estado do worker e o broker. A leitura aceita é durável, ainda que o processamento não tenha começado. |
| Revisão devolveu `409` | Recarregue a avaliação. Não reenvie cegamente outra versão: o operador precisa conferir a evidência nova. |
| `401` ou `403` | Confirme a chave correta para a operação; não imprima `Authorization` para diagnosticar. |
| Mensagem rejeitada sem reenvio automático | Inspecione a fila de erro do RabbitMQ e correlacione com os logs do worker. Corrija a causa antes de qualquer redrive manual. |

Os logs do worker correlacionam por identidade da mensagem e viagem. Não use o dashboard do broker como interface pública; a administração permanece indisponível fora da rede do laboratório.

## Verificar recuperação e reentrega

Em um ambiente descartável, use um projeto explícito, com um banco limpo e sem outro Compose ocupando a porta configurada:

```sh
docker compose -p sinalfrio-qa up -d --build --wait
python3 scripts/demo.py -p sinalfrio-qa --resilience --verify-republication
docker compose -p sinalfrio-qa down --volumes
```

O segundo comando para e restaura apenas o broker do projeto verificado. Depois, simula a perda da marca de publicação de um único item de teste: a mesma mensagem cruza o RabbitMQ de novo e a inbox continua com uma entrada. A revisão concorrente admite uma gravação. A role de runtime pode inserir, mas não editar ou apagar revisões nem criar tabelas. A reposição da marca é uma técnica do teste, não um procedimento recomendado para produção.
