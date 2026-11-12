# Viabilidade, suporte e dependências

Avaliação documental de 12/11/2026. A Fase 0 estabelece uma base técnica. Validação física e compatibilidade de pacotes pelo build continuam pendentes.

## Matriz de suporte e limites

| Área | Alpha planejada | Condição ou limite |
| --- | --- | --- |
| Windows 11 x64 | Plataforma prioritária | Testar versão ainda suportada e hardware real |
| Windows 10 | Avaliação separada | Host observado é Pro 22H2, build 19045.6466. Elegibilidade de suporte/ESU ainda desconhecida |
| ARM64 | Futura | Sem validação |
| USB, analógico, HDMI/DP, Bluetooth, wireless e virtual | Enumerar endpoints expostos | Cobertura real por família será registrada nos testes |
| Identificação | Nome, ID, estado, padrões e propriedades disponíveis | P2/P3 pode identificar a placa. Modelo, fabricante e driver dependem de propriedades expostas |
| Volume/mute endpoint | Viável por Core Audio | Alcance em modo exclusivo depende de hardware/driver |
| Balanço | Condicional | Exige controle e identificação dos canais L/R |
| Sessões e seus volumes/mute | Viável para sessões expostas | Múltiplas sessões por aplicativo, encerramentos e captura exigem tratamento específico |
| Medidores | Condicional à API | Mostrar picos reais ou indicação de indisponibilidade |
| Hotplug e alterações externas | Viável por notificações/reconciliação | Validar USB, Bluetooth, troca de padrão e suspensão |
| Preferências JSON | Viável localmente | Testar corrupção, backup, migração e falhas de escrita |
| DSP global e mic externo processado | Fases 2–3 | Dependem de rota comprovada e integração virtual/APO |
| Phantom power/ganho analógico/7.1 físico | Dependente de hardware | Exibir somente capacidades documentadas e expostas |

Instalação do SDK e presença do runtime foram confirmadas no Windows 10 local. A política do .NET depende também do ciclo de vida do sistema. Compatibilidade de execução e suporte do fabricante precisam ser registrados separadamente para essa máquina. [Sistemas e instalação do .NET](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## Dependências previstas

| Componente | Escolha inicial | Licença e validação |
| --- | --- | --- |
| .NET SDK/WinForms | Linha 10.0, SDK local 10.0.404 | Repositórios .NET/WinForms MIT, manter avisos aplicáveis da distribuição |
| NAudio | 3.1.0 estável como versão candidata fixa | MIT, pacote declara .NET 9 ou superior. Confirmar restauração e wrappers de Core Audio no incremento inicial |
| JSON | `System.Text.Json` do runtime | Evita dependência JSON adicional |
| Testes | `xunit.v3` 4.0.3 como candidato | Apache-2.0. Selecionar runner/SDK de testes compatíveis com `dotnet test` ao criar o projeto e registrar versões exatas |
| Git | 2.56.0.windows.4 instalado | Ferramenta de desenvolvimento, GPLv2, sem redistribuição planejada com o app |

NAudio 3.1.0 é uma escolha conhecida para avaliação, sujeita à conferência de APIs e dependências transitivas na implementação. [Pacote e compatibilidade](https://www.nuget.org/packages/NAudio/3.1.0), [licença MIT](https://github.com/naudio/NAudio/blob/main/license.txt).

O pacote xUnit candidato suporta frameworks modernos. A licença do projeto é Apache-2.0. [Pacote](https://www.nuget.org/packages/xunit.v3/4.0.3), [licença](https://github.com/xunit/xunit/blob/main/LICENSE).

Fontes de licença do runtime e da UI devem integrar o inventário de distribuição. [Runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WinForms](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT). A licença do próprio Wave Studio ainda precisa ser definida pelo proprietário.

Ao adicionar pacotes, fixar versões, gerar lock file, conferir dependências transitivas e avisos de vulnerabilidade/licença e executar restore/build/test. Esta etapa consultou documentação e metadados, sem baixar pacotes.

## Dependências futuras e pontos de decisão

WDK/C++ e SysVAD ficam reservados ao desenvolvimento nativo justificado. O repositório de exemplos usa licença MIT, sujeita aos avisos de cada arquivo e aos termos separados das ferramentas e assinatura. Um exemplo exige adaptação e validação para funcionar como cabo virtual do produto. [Código e licença](https://github.com/microsoft/Windows-driver-samples).

RNNoise, WebRTC, HRTF, pitch/formantes, codecs, APOs externos e cabos virtuais serão avaliados nas fases correspondentes. A seleção deve incluir licença do código, modelos/dados, dependências e direito de redistribuição. Equalizer APO e produtos comerciais permanecem referências ou candidatos de integração documentada. Nenhum deles foi escolhido como dependência da Alpha.

O risco principal do produto completo é entregar DSP a aplicativos externos com isolamento, baixa latência e restauração confiável. A decisão preferida de endpoints virtuais e suas alternativas estão em [ARCHITECTURE.md](ARCHITECTURE.md). A aprovação técnica dessa integração exige os ensaios da Fase 3.
