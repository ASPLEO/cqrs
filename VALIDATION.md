# Validação local — 18/10/2026

Ambiente: Windows, SDK .NET 10.0.402, configuração Release, alvo net10.0.

- Restore e compilação da solução concluídos.
- 23 testes aprovados, zero falhas e zero ignorados. Cobrem commands sem retorno, retorno tipado,
  valores, interfaces e nulos; handlers internos; exceções originais síncronas e assíncronas;
  cancelamento antes e durante a execução e encaminhamento de token nas duas sobrecargas;
  isolamento de dependências entre escopos e providers; registro repetido; argumentos inválidos;
  conflitos de handlers e de lifetimes; handlers ausentes e duplicidade introduzida manualmente.
- Dois pacotes 0.1.0-preview.1 gerados e inspecionados pelo script: README e DLL net10.0 presentes,
  Abstractions sem dependências e DependencyInjection com dependência da versão coordenada de Abstractions.
- Consumidor restaurado com cache novo e source mapping que exige feed local para Aspleo.Cqrs.*.
  Usa PackageReference para os dois pacotes, sem ProjectReference. Command e query executados:
  `Consumo NuGet validado: contador = 1.`
- Origem utilizada somente em operações de leitura; nenhum arquivo ou dependência do Owner alterado por esta tarefa.

Pacotes: `artifacts/packages/0.1.0-preview.1/` (arquivos locais ignorados pelo Git).
Reprodução: `dotnet test Aspleo.Cqrs.slnx -c Release` e `pwsh -File scripts/Verify-Packages.ps1`.

Os pacotes locais contêm `Authors=Corporação Aspleo`, conforme confirmação do responsável, sob liderança
de Alan Silva Portugal. A licença MIT foi confirmada, configurada como expressão SPDX e incluída como
arquivo LICENSE nos dois pacotes. O script valida esses metadados e a presença do arquivo; o workflow
verifica autoria, licença e a correspondência entre tag e versão.

Não foram executados workflows no GitHub, autenticação remota ou publicação; não houve push nem envio
de tags. As permissões efetivas do repositório/organização e o acesso ao feed ainda exigem validação remota.
Não foram testados Linux, trimming ou Native AOT. O workflow foi preparado para Linux, mas só a execução
local em Windows foi realizada nesta tarefa. A migração do Owner e os testes de negócio são trabalho futuro.
