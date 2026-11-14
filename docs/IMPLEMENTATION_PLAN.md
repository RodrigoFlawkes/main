# Plano da v0.1.0 Alpha

Estado em 14/11/2026: F2-I5 implementada isoladamente. Descritores/probe WASAPI reais somente leitura, adaptação de relógio com evidência explícita e sessão de transporte simulado que exercita FIR adaptativo, SPSC, priming, ocupação temporal, parada e recuperação. Regressão dos 375 anteriores preservada; verificações finais em PROJECT_STATUS/TEST_PLAN. [Contrato, APIs e bloqueios](WASAPI_BACKEND_PLAN.md). Prévia corrigida republicada localmente pelo fluxo autorizado, sem release pública.

Parar após F2-I5 para revisão. Proposta F2-I6: resolver lacuna pública do clock/HRESULT, confirmar negociação real sem conversão implícita, ownership e fechamento, interlock compartilhado com diagnóstico e eventual conexão física opt-in. Não abrir streams novos nesta etapa nem liberar integração física sem revisão, regressão A01–A12 e autorização. Validar Windows 11; Windows 10 experimental. Não antecipar EQ, Fase 3 ou drivers. Consulta de mix/capacidade não é negociação efetiva nem validação física.

As referências anteriores a Fase 2 fora do escopo registram etapas históricas da identidade/Alpha; a autorização atual permite apenas F2-I5 isolada e republicação local corrigida. O redesign profissional continua posterior à consolidação das funcionalidades reais. O FIR nominal e o adaptativo da F2-I4 conservam suas metas/regressão.

Identidade oficial aplicada em 13/11/2026 após a entrega técnica F1-I6-METER: original preservado, ICO multirresolução, recursos incorporados, cabeçalho/README e metadados. Regressão atual de 252 testes por configuração. Distribuição pública, instalador, redesign completo e Fase 2 continuam fora desta etapa. Guia em [assets/branding](../assets/branding/README.md).

Planejamento atualizado em 13/11/2026. Fase 1 em andamento, com incrementos verificáveis. Escopo detalhado em [REQUIREMENTS.md](REQUIREMENTS.md).

Incrementos 1–5 publicados e revisados. F1-I6 fornece checkpoint técnico com correções da revisão F1-I5, picos por sessão a 1 Hz, publicação Release x64 e roteiro de aceitação. F1-I6-MIC acrescentou captura curta em memória e reprodução explícita. O adendo F1-I6-METER apresenta o pico real da entrada também na aba de teste, reutilizando o worker existente. Builds Debug/Release com zero erros/avisos e 248 testes aprovados em cada configuração, incluindo os 228 anteriores. Aceitação física/visual, A11/A12 e Windows 11 permanecem pendentes. A Fase 1 continua em validação.

Os adendos usam os projetos e dependências existentes. F1-I6-METER não abre captura automaticamente nem acrescenta outro worker/stream de áudio. Após publicação, receber resultados A01–A12 por sistema/dispositivo, concluir os subcasos A11 e comparar o medidor A12 com o Windows. Sucesso na função principal de gravação/reprodução foi relatado após ajustes próprios não especificados; a aceitação completa continua pendente. Monitoramento ao vivo permanece no planejamento da Fase 2, que não foi iniciada.

## Incrementos

F1-I6 executa o escopo técnico de NEXT_TASK.md e REVIEW.md sincronizados até 8d0e688. Arquivos excessivos têm proteção própria, falhas previsíveis de gravação são tratadas, seleção aguarda carga inicial e restauração confere balanço após todos os comandos. Picos por sessão reutilizam o ciclo de consulta do mixer, com wrappers locais à operação e pausa/atraso explícitos. Publicação framework-dependent AMD64 e ensaio de dez minutos do processo estão registrados em ALPHA_ACCEPTANCE.md. Preferências duráveis de sessão aguardam identidade inequívoca. As etapas abaixo preservam os critérios de aceitação física ainda pendentes.

1. Criar `WaveStudio.slnx`, Desktop, Audio e testes conforme a arquitetura. Fixar SDK e dependências após conferir APIs e runner. Abrir janela pt-BR, enumerar endpoints reais, mostrar estados e padrões e permitir seleção independente. Critério de saída — restore/build/test bem-sucedidos e comparação manual da enumeração com o Windows.
2. Adicionar volume/mute por endpoint e balanço condicional. Sincronizar mudanças externas, validar intervalos e erros de dispositivo. Critério — alterações verificadas no Windows e teste físico dos canais, incluindo mono e suporte ausente.
3. Listar sessões reais do endpoint, identidade, estado e nome/processo quando disponíveis. Adicionar volumes/mute e tratar sessões criadas/encerradas durante o uso. Critério — dois aplicativos independentes e sons do sistema controlados corretamente.
4. Adicionar medidores de pico reais, atualização moderada, notificações de hotplug/padrão e recuperação. Critério — reprodução e fala movimentam os medidores disponíveis, desconexão/reconexão e suspensão são tratadas sem travamento ou uso de objetos descartados.
5. Persistir configurações versionadas, preferências por endpoint e identidade segura para preferências de sessão quando disponível. Implementar backup, leitura tolerante a corrupção e restauração explícita de valores. Critério — reinício recupera seleções e preferências, preserva arquivo incompatível e informa falhas de escrita.
6. Executar a matriz de aceitação, corrigir erros e atualizar documentação com evidências. Gerar executável x64 `WaveStudio.exe` e instruções de execução. Critério — distinguir testes automáticos, físicos aprovados e limitações por dispositivo antes de identificar a versão como Alpha funcional.

Cada incremento inclui testes relevantes e atualização de status/changelog. Falha de suporte por endpoint deve ter explicação visível. Ganho digital, EQ, mesa, roteamento, soundboard e demais telas entram conforme as fases posteriores ganhem implementação.

## Verificação da solução

Executar na raiz, em PowerShell.

```powershell
dotnet restore WaveStudio.slnx --locked-mode
dotnet build WaveStudio.slnx -c Debug --no-restore
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
dotnet test --solution WaveStudio.slnx -c Debug --no-build
dotnet run --project src/WaveStudio.Desktop
```

`restore` verifica as dependências fixadas, `build` compila, `test` executa os testes e `run` inicia a janela. A opção `--solution` é usada pelo modo MTP do SDK .NET 10. Publicação self-contained e instalador serão avaliados posteriormente com licenças e suporte do sistema.

## Sequência posterior

Fase 2 constrói captura/reprodução, buffers, resampling, monitoramento opcional, gravação de teste e medição de latência. Fase 3 comprova DSP de navegador e microfone externo em OBS/Discord. O resultado dessa prova condiciona a expansão de efeitos. Seguir então as Fases 4–12 dos requisitos, passando por mesa, barramentos, EQ/dinâmica, tratamento vocal, soundboard, efeitos de voz, surround, gravação multifaixa e distribuição.

## Direção atual: funcionalidade antes do redesign profissional

O proprietário solicitou consolidar primeiro o produto funcional bruto, preservando a marca e a interface simples/acessível durante o desenvolvimento. O redesign profissional será etapa posterior à consolidação das funcionalidades; ajustes de usabilidade necessários continuam permitidos. O EQ gráfico de 10 bandas, presets, EQ paramétrico e 7.1 virtual speaker shifter/HRTF permanecem nos requisitos das fases correspondentes, sem antecipar controles fictícios. A sequência técnica Fases 2–12 não é alterada. A fundação isolada F2-I1 pode ser desenvolvida com sinal sintético enquanto a aceitação física da Fase 1 permanece pendente. Não declarar aceitação da Alpha nem integrar um novo pipeline contínuo à UI antes da revisão e regressão física da base. A etapa não envolve driver, captura automática, áudio externo ou release pública.

## Prioridade atual — primeira função audível e economia de cota

14/11/2026, após F2-I5/ad504f8: fundação técnica avançada, Fase 2 não encerrada (clock nativo, transporte físico e monitoramento pendentes). Proprietário pede funções e aceita infraestrutura indispensável, com esforço Alto para economia. Adendo EQ-PREVIEW-1: prova local de EQ de 10 bandas em gravação curta existente, A/B original/processado e preamp, sem efeito global, microfone virtual, stream contínuo ou expansão em massa. Mesmo DSP reutilizável nos EQs independentes de entrada/saída e mixer. F2-I6 nativa será retomada após a prova audível para destravar processamento contínuo e prova externa da Fase 3; não cancelar nem antecipar efeitos em massa. A01–A12, Windows 11, segurança, regressão e autorização permanecem. Design profissional por último. Planejar tarefas curtas, avaliar saldo observado depois de cada entrega, não prometer consumo percentual específico.

## Adendo do proprietário — Hub e novos módulos (14/11/2026)

Consultar [PRODUCT_HUB_PLAN.md](PRODUCT_HUB_PLAN.md) para navegação lateral futura, organização de áreas, dois novos módulos (Reparos e Apoiar) e refinamento dos presets de voz. Complemento de planejamento, sem modificar REQUIREMENTS.md ou ampliar EQ-PREVIEW-1. Mesa virtual não equivale a controles fictícios; recursos indisponíveis ficam ocultos/desabilitados. Diagnóstico somente leitura primeiro, correções de Windows/driver apenas com suporte oficial, consentimento e recuperação. Doação voluntária após definição de link/plataforma pelo proprietário. Design profissional depois das funções reais. Manter economia e sequência técnica acordadas.

## Adendo — catálogo visual e aparência (14/11/2026)

[PRODUCT_HUB_PLAN.md](PRODUCT_HUB_PLAN.md) registra Meu Equipamento, associação visual independente de endpoint, catálogo local expansível, imagens licenciadas/personalizadas, conexão/estado e bateria apenas se realmente disponível, além de Claro/Escuro/Alto contraste/Seguir Windows, cor personalizada, wallpaper e persistência. Escopo posterior de design; não ampliar EQ-PREVIEW-1, não gastar cota implementando agora nem prometer bateria universal. Manter a prova de processamento externo e funções reais primeiro.
