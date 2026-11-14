# Componentes da prévia Alpha

Inventário da publicação local Release x64, atualizado na EQ-PREVIEW-1. O EQ reutiliza BiQuadFilter do NAudio.Core já listado. Versões resolvidas pelos arquivos de lock e metadados dos pacotes instalados; nenhuma dependência nova nesta etapa.

| Componente | Versão | Licença e avisos |
| --- | --- | --- |
| NAudio.Core.dll | 3.1.0 | MIT, copyright 2008–2026 Mark Heath. Texto integral em licenses/NAudio-MIT.txt |
| NAudio.Wasapi.dll | 3.1.0 | MIT, mesmo texto do projeto NAudio |
| System.Numerics.Tensors.dll | 9.0.0 | MIT, .NET Foundation and Contributors. LICENSE.TXT e THIRD-PARTY-NOTICES.TXT do pacote acompanham a pasta licenses da publicação |
| Apphost WaveStudio.exe | SDK .NET 10.0.404 / host local x64 | MIT do .NET, texto em licenses/DotNet-MIT.txt |
| Microsoft.NETCore.App e Microsoft.WindowsDesktop.App | Linha 10 x64, instalados separadamente | Runtime externo à pasta publicada. O host de validação possui 10.0.15 |

A pasta de publicação inclui somente as dependências do aplicativo e seus arquivos de execução/documentação. Testes usam xUnit v3 4.0.3 e Microsoft Testing Platform 2.5.2 no ambiente de desenvolvimento. A licença própria do Wave Studio aguarda definição do proprietário. Esta entrega fornece uma prévia para teste local. Redistribuição pública, assinatura e instalador serão avaliados em etapa própria.

O NAudio declara MIT nos nuspec de NAudio.Core/NAudio.Wasapi 3.1.0. O texto integral foi conferido na tag fixada. [NAudio 3.1.0](https://github.com/naudio/NAudio/blob/v3.1.0/LICENSE).

O pacote System.Numerics.Tensors 9.0.0 contém seus próprios LICENSE.TXT e THIRD-PARTY-NOTICES.TXT. Esses textos são copiados integralmente para a distribuição local. O apphost e o runtime pertencem ao projeto .NET, com seus avisos aplicáveis. [Licença do runtime .NET](https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT), [licença WinForms](https://github.com/dotnet/winforms/blob/v10.0.0/LICENSE.TXT).

Reprodução do pacote na máquina Windows x64 de desenvolvimento usa os comandos de publicação e cópia de avisos registrados em ALPHA_ACCEPTANCE.md. A arquitetura AMD64 é conferida no cabeçalho PE do executável gerado.
