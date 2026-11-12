# Wave Studio — instruções para agentes de código

## Missão e contexto
Wave Studio é uma aplicação desktop de gerenciamento, processamento e mixagem de áudio para Windows, com compatibilidade independente de fabricante. Repositório: Wave-Universal-Studio/main. Leia `docs/REQUIREMENTS.md` antes de planejar ou implementar. O proprietário tem conhecimentos básicos de Java e JavaScript e está aprendendo C#; comunique-se em português do Brasil e explique comandos e decisões de forma acessível.

## Stack e decisões iniciais
- C# / .NET 10 LTS, Windows Forms, x64, NAudio e Windows Core Audio/WASAPI. JSON versionado para configurações; Git e xUnit para versionamento e testes. Use C++/WDK apenas quando a arquitetura de APO/driver exigir. Priorize Windows 11; valide a viabilidade de Windows 10 separadamente, sem prometer suporte a versões fora do suporte do sistema/.NET.
- Inspecione o SDK, Git e arquivos existentes antes de criar a solução. Use versões estáveis e compatíveis de dependências; verifique documentação oficial e licença. Não invente APIs.
- Comece com poucos projetos e responsabilidades claras; separe UI, abstrações/modelos, acesso ao áudio, DSP e mixagem quando necessário. Não crie bibliotecas vazias apenas para reproduzir uma árvore sugerida.
- Mantenha processamento de áudio independente da UI, sem I/O bloqueante, alocações desnecessárias ou rede no callback de áudio. Trate cancelamento, descarte, desconexão e mudança de dispositivo.

## Fluxo obrigatório
1. Leia este arquivo, `docs/REQUIREMENTS.md`, `docs/ARCHITECTURE.md` e `PROJECT_STATUS.md` (crie os dois últimos se ainda não existirem). Inspecione `git status` e preserve alterações do usuário.
2. Trabalhe em uma etapa pequena do roadmap. Informe objetivo, plano e limitações; implemente a etapa quando autorizado. Não tente implementar todo o produto em um turno.
3. Altere somente o necessário; nunca apague trabalho existente nem faça reset/force push. Não faça commit/push sem solicitação explícita. Não use segredos no código, logs ou Git.
4. Solicite autorização antes de instalar programas/drivers, elevar privilégios, alterar configurações críticas do Windows, realizar operações destrutivas ou baixar/executar binários não verificados. Não desative Secure Boot, assinatura de drivers ou integridade de memória.
5. Restaure dependências, compile e rode testes disponíveis. Comandos típicos após criar a solução: `dotnet restore`, `dotnet build`, `dotnet test`. Registre comandos e resultados reais; não afirme testes manuais de áudio que não executou. Explique como iniciar o app com `dotnet run --project src/WaveStudio.Desktop` quando esse projeto existir.
6. Atualize `PROJECT_STATUS.md`, `CHANGELOG.md` e documentação relevante. Diferencie implementado, compilado, testado automaticamente, pendente de teste físico e bloqueado. Não declare concluído por ter apenas uma UI.

## Limites técnicos inegociáveis
- Controle de volume e sessões do Windows não equivale a roteamento nem a DSP global. WASAPI Loopback sozinho não substitui o áudio original. Um microfone processado utilizável em Discord/OBS depende de endpoint de captura virtual, APO de captura ou integração equivalente validada. Planeje cedo uma prova real de reprodução e captura externas.
- Não prometa controle de hardware, Phantom Power, ganho analógico, identificação de modelo analógico ou 7.1 físico quando não houver suporte exposto. Não use injeção em jogos, drivers proprietários copiados ou APIs privadas sem avaliar riscos e documentar alternativas suportadas.
- Recursos não implementados devem ficar ocultos ou desabilitados com explicação. Nenhum slider fictício. Separe volume do Windows, ganho digital e volume do mixer. Comece com ganhos neutros, efeitos e monitoramento desligados; evite feedback e clipping. Não grave áudio sem ação do usuário.
- Respeite privacidade: processamento local, sem telemetria de áudio, conta obrigatória ou envio para nuvem. Não copie marcas, interface ou código proprietário de produtos de referência. Verifique licenças antes de distribuir.

## Primeira entrega (v0.1.0 Alpha)
Crie/atualize README, arquitetura, status, changelog e plano de testes. Implemente uma janela WinForms em pt-BR que enumere endpoints de reprodução/captura, mostre padrão, permita seleção, controle volume/mute e balanço quando suportado, liste sessões do endpoint e controle seus volumes/mute, mostre medidores reais, reaja a conexões/desconexões e salve configurações localmente. Sem driver próprio nessa etapa. Verifique o build, testes disponíveis e forneça passos de teste local. Não indique que efeitos globais ou mesa multicanal estão prontos nesta versão. Consulte a sequência de fases e critérios em `docs/REQUIREMENTS.md`.
