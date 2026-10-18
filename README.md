# Aspleo.Cqrs

Biblioteca CQRS em processo para .NET 10, extraída do Owner. Cada request possui um único
handler para seu tipo concreto e retorno esperado. Não inclui eventos, pipelines, retries,
filas, persistência ou execução em segundo plano.

Autoria: Corporação Aspleo, sob liderança de Alan Silva Portugal.

Licença: [MIT](LICENSE), conforme o [texto da Open Source Initiative](https://opensource.org/license/mit).

## Estrutura

- `src/Aspleo.Cqrs.Abstractions`: requests, handlers e `IDispatcher`, no namespace `Aspleo.Cqrs`, sem dependências NuGet.
- `src/Aspleo.Cqrs.DependencyInjection`: dispatcher e invocadores internos, descoberta e registro, no namespace `Aspleo.Cqrs.DependencyInjection`. Depende apenas de Abstractions e Microsoft DI Abstractions.
- `tests/Aspleo.Cqrs.Tests`: testes sem dependências do backend.
- `examples/LocalConsumer`: consumidor console com PackageReference para os dois pacotes, sem ProjectReference.
- `scripts/Verify-Packages.ps1`: gera e inspeciona os pacotes, restaura o consumidor com feed local e cache novo, e o executa.
- `.github/workflows`: integração contínua e publicação por tag.

## Uso

Instale `Aspleo.Cqrs.DependencyInjection` versão `0.1.0-preview.1`. Abstractions é transitivo;
projetos que só definem contratos podem referenciá-lo diretamente. Um console também precisa
do contêiner `Microsoft.Extensions.DependencyInjection`; hosts ASP.NET Core já o fornecem.

```csharp
using Aspleo.Cqrs;
using Aspleo.Cqrs.DependencyInjection;

services.AddCqrs(
    typeof(CriarUsuarioCommand).Assembly,
    typeof(ListarPlanosQuery).Assembly);

var resultado = await dispatcher.Send(query, cancellationToken);
await dispatcher.Send(command, cancellationToken);
```

```csharp
namespace MinhaAplicacao;

public sealed record ConsultarNomeQuery(Guid Id) : Aspleo.Cqrs.IRequest<string?>;

internal sealed class ConsultarNomeHandler
    : Aspleo.Cqrs.IRequestHandler<ConsultarNomeQuery, string?>
{
    public Task<string?> Handle(ConsultarNomeQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>("Exemplo");
    }
}
```

Um programa completo está em `examples/LocalConsumer/Program.cs`. Também existem
`AddRequestHandlers(assemblies)` e `AddRequestDispatcher()` para compor módulos separadamente.
O scanner inclui classes internas com construtores públicos; ignora abstratas e genéricos abertos.
Repetir o registro não duplica serviços. Conflitos são rejeitados antes de modificar a coleção.
Registros manuais preexistentes devem ser únicos, do mesmo tipo e scoped. Duplicidades
adicionadas depois também são detectadas pelo dispatcher durante o envio.

Handlers e dispatcher são scoped. Resolva o dispatcher no escopo da operação; ele não cria outro
escopo. O cache estático guarda somente invocadores sem estado, sem handlers, providers ou resultados.
Requests nulas são rejeitadas. Tokens já cancelados impedem o despacho; o mesmo token é encaminhado
ao handler, que deve cooperar com o cancelamento durante a execução. Exceções síncronas e assíncronas
permanecem intactas. Handler ausente ou múltiplo gera InvalidOperationException com diagnóstico do tipo.
Não há fallback para handlers de classes base nem despacho por object.
Reflexão é usada na descoberta e na criação dos invocadores. Native AOT e trimming não foram validados.

## Validação local

Requer SDK .NET 10 e PowerShell 7 para o script.

```powershell
dotnet restore Aspleo.Cqrs.slnx
dotnet build Aspleo.Cqrs.slnx -c Release --no-restore
dotnet test Aspleo.Cqrs.slnx -c Release --no-build
pwsh -File scripts/Verify-Packages.ps1
```

Os dois `.nupkg` ficam em `artifacts/packages/0.1.0-preview.1/`.
O script verifica README, DLL net10.0, versões e dependência interna; Abstractions deve ter zero
dependências. O consumidor fica fora da solução para permitir compilar antes de empacotar.
O source mapping obriga os pacotes próprios a virem do feed local, com cache isolado a cada execução.
O consumidor executa command e query usando o mesmo estado scoped.
O cache em `artifacts/consumer-cache/` pode ser removido após a validação.
As versões são coordenadas por `Directory.Build.props`; o script também aceita `-Version`.

## Feed GitHub Packages

O remote existente identifica `https://github.com/ASPLEO/cqrs` como repositório.
Copie `examples/nuget.config` para a raiz do consumidor. Ele não contém credenciais e direciona
`Aspleo.Cqrs.*` ao feed `https://nuget.pkg.github.com/ASPLEO/index.json`, mantendo os demais pacotes
no nuget.org. O padrão específico prevalece sobre `*`. Esta biblioteca não precisa do feed privado
para compilar; mantenha essa configuração nos consumidores.

Desenvolvedores devem usar PAT **classic** com `read:packages`, acesso ao pacote e SSO quando exigido.
Configure as credenciais fora dos arquivos versionados, por gerenciador de credenciais ou ambiente:

```powershell
# Valores obtidos de um gerenciador de segredos, nunca gravados no repositório.
$env:NuGetPackageSourceCredentials_github = "Username=$env:GITHUB_USER;Password=$env:GITHUB_PACKAGES_TOKEN;ValidAuthenticationTypes=Basic"
dotnet restore --configfile nuget.config
```

Para outras esteiras GitHub Actions, conceda acesso de leitura ao repositório consumidor nas configurações
do pacote (Manage Actions access), declare `contents: read` e `packages: read` e injete a credencial:

```yaml
permissions:
  contents: read
  packages: read
steps:
  # Após checkout e setup-dotnet:
  - run: dotnet restore --configfile nuget.config
    env:
      NuGetPackageSourceCredentials_github: Username=${{ github.actor }};Password=${{ secrets.GITHUB_TOKEN }};ValidAuthenticationTypes=Basic
```

Fora do GitHub Actions, use um segredo protegido com PAT classic de leitura. O registro NuGet GitHub
exige autenticação também para consumo público. Consulte a
[documentação oficial](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry).

## Primeira publicação

1. A autoria está configurada como `Corporação Aspleo`, sob liderança de Alan Silva Portugal.
   A licença MIT está definida em `PackageLicenseExpression`, com o texto em `LICENSE` incluído nos dois pacotes.
2. Confirme repositório e feed, habilite Actions e verifique se a organização permite a publicação e o acesso desejado.
3. Execute os testes e a validação local. Revise e envie o código ao GitHub quando autorizado.
4. Crie e envie a tag `v0.1.0-preview.1` no commit revisado. A tag deve coincidir exatamente com `Version`
   em `Directory.Build.props`. Atualize essa propriedade antes de tags futuras.
5. O workflow de publicação compila, testa, valida o consumidor e envia os dois pacotes usando `GITHUB_TOKEN`,
   com somente `contents: read` e `packages: write`. Não é necessário PAT para publicar por esse workflow.

As duas publicações não são uma transação. Em caso de falha parcial, a reexecução usa `--skip-duplicate`
para completar o envio. CI e publicação preservam os pacotes como artefatos.
Esta tarefa não fez push, não enviou tags e não publicou no GitHub Packages.

## Migração futura do Owner

O projeto de origem foi usado somente para leitura. Em uma tarefa posterior:

1. Configure o feed e o acesso no Owner e nas suas esteiras.
2. Adicione Abstractions aos projetos com requests/handlers e DependencyInjection à composição/infraestrutura,
   todos na mesma versão.
3. Troque `Shared.Core.Cqrs` por `Aspleo.Cqrs` e os imports de extensões por `Aspleo.Cqrs.DependencyInjection`.
   Preserve os registros por módulo com `AddRequestHandlers` e `AddRequestDispatcher`, ou use `AddCqrs` no host.
4. Remova contratos, dispatcher e extensões locais substituídos. Os tipos antigos e novos são distintos;
   migre todos os requests e handlers envolvidos juntos.
5. Preserve as dependências de negócio e os escopos. Execute testes dos módulos e endpoints, incluindo
   autorização e unidade de trabalho, antes de concluir a migração.

Resultados locais estão em `VALIDATION.md`. Execução dos workflows, autenticação e permissões remotas
ainda precisam ser verificadas no GitHub. Autoria e licença foram confirmadas pelo responsável.
