# Plano de testes

Estado em 12/11/2026 — Incremento 1 implementado e verificações abaixo executadas. Os casos das etapas posteriores permanecem planejados.

## Resultados do Incremento 1

- `dotnet restore WaveStudio.slnx` passou após repetir com acesso autorizado ao NuGet fora do sandbox. `dotnet restore WaveStudio.slnx --locked-mode` também passou.
- `dotnet build WaveStudio.slnx -c Debug --no-restore` passou com zero erros e zero avisos.
- `dotnet test --solution WaveStudio.slnx -c Debug --no-build --output Detailed`, com telemetria MTP desativada na sessão, aprovou 16 testes, zero falhas e zero ignorados. Foram 14 casos de regras/mensagens e 2 testes de integração Core Audio.
- A integração enumerou 67 endpoints reais, com zero avisos. Observou estados ativo/ausente/desconectado, padrão de reprodução multimídia diferente do de comunicações, captura padrão e formatos de mixagem. Consultas concorrentes serializadas e consulta após cancelamento também passaram.
- `WaveStudio.exe` foi iniciado. A acessibilidade da janela confirmou nome/versão, seletores, dados de entrada e saída, botão Atualizar e status de 67 endpoints. A captura de tela falhou com `FrameArrived timed out` e `window capture timed out`. O clique automatizado falhou com `coordinate input geometry is unavailable`. A conferência visual e das interações permanece pendente.

Os testes `WindowsIntegration` consultam a configuração real do host. Um inventário vazio é válido. Avisos de falha de API reprovam esses testes e exigem diagnóstico local. Para executar apenas regras, use `dotnet test --solution WaveStudio.slnx --filter-not-trait Category=WindowsIntegration`.

## Passos físicos do Incremento 1

1. Executar `dotnet run --project src/WaveStudio.Desktop`. Conferir título, versão, textos, legibilidade e navegação por teclado em 100%, 150% e 200% de escala.
2. Comparar nomes, estados e padrões de multimídia/comunicações das duas listas com as configurações de som do Windows. Dispositivos históricos podem aparecer como ausentes. Ativos aparecem primeiro.
3. Selecionar outro headset e conferir nome, ID, estado, canais, taxa e bits do mix. Confirmar que a seleção de microfone e o padrão do Windows continuam os mesmos. Repetir trocando somente o microfone.
4. Clicar em Atualizar e conferir preservação das duas seleções. Alterar um padrão explicitamente pelo Windows e atualizar para conferir sua identificação.
5. Remover um USB selecionado e clicar em Atualizar. Conferir estado ausente/desconectado ou aviso de indisponibilidade. Reconectar, atualizar e conferir recuperação pelo mesmo ID quando preservado pelo Windows.
6. Consultar um endpoint inativo. Conferir indicação de formato indisponível. Fechar a janela durante uma atualização e reabrir para conferir estabilidade.

Registrar modelo/conexão, versão do Windows e resultado por passo. Repetir em Windows 11 x64 suportado. No Windows 10 local, os resultados atuais cobrem execução experimental e consultas reais. Os passos de volume, sessões e medidores abaixo pertencem aos incrementos seguintes.

## Testes automatizados planejados

| Área | Casos mínimos |
| --- | --- |
| Configurações | Round-trip JSON, padrão, migração conhecida, versão futura, corrupção, backup, arquivo ausente, falha de escrita e gravações concorrentes serializadas |
| Volume e balanço | Limites, centro neutro, extremos com atenuação, preservação do nível-base, mono, multicanal sem L/R e suporte ausente |
| Estado de dispositivos | Preferência ausente, seguir padrão, troca de seleção, eventos repetidos, remoção durante comando e descarte idempotente |
| Sessões | Criação/encerramento, PID reutilizado, sons do sistema, múltiplas sessões, identidade indisponível e isolamento das alterações |
| Apresentação | Medidor indisponível, atualização externa sem realimentação de comandos, resposta atrasada de seleção anterior e erro recuperável |

Testes com serviços simulados verificam regras e estados. Testes de integração Windows exercitam Core Audio, com seus resultados identificados separadamente.

## Aceitação física da Alpha

Preparar volume confortável, anotar valores iniciais do Windows e aplicativos e restaurá-los ao final. Usar fones quando necessário. Registrar sistema, versão do app, dispositivo/conexão, suporte exposto, passos, resultado e evidência por caso.

| Caso | Procedimento e resultado esperado |
| --- | --- |
| T01 Enumeração | Comparar entrada/saída, IDs, estados e padrões de multimídia/comunicações com o Windows. Propriedades ausentes são informadas |
| T02 Seleção | Selecionar entrada e saída separadamente. Somente o alvo administrado muda, sem alterar padrão do sistema |
| T03 Volume/mute | Alterar no app e conferir no Windows. Alterar no Windows e conferir atualização do app. Exercitar entrada e saída |
| T04 Balanço | Usar sinal estéreo conhecido e verificar centro/esquerda/direita. Mono ou suporte ausente desabilita o controle com explicação |
| T05 Sessões | Abrir dois aplicativos reproduzindo áudio. Ajustar volume/mute de uma sessão, conferir isolamento, encerrar e reabrir o aplicativo |
| T06 Medidores | Reproduzir áudio e falar no microfone. Picos reais reagem quando disponíveis, silêncio retorna a zero, indisponibilidade é explícita |
| T07 Hotplug | Remover/reconectar USB durante leitura e mudança de volume. UI permanece responsiva e reconcilia estado sem trocar preferências silenciosamente |
| T08 Padrão/retomada | Alterar padrão no Windows, suspender/retomar e testar ausência de endpoints. Reconexão recupera serviços e controles válidos |
| T09 Persistência | Reiniciar, conferir preferências e restauração explícita. Testar JSON inválido/futuro e pasta sem escrita, preservando dados recuperáveis |
| T10 Robustez/UI | Abrir/fechar repetidamente, trocar endpoints rapidamente, usar teclado e escala 100/150/200%. Verificar erros, layout e consumo observado |
| T11 Privacidade | Conferir recursos apresentados, ausência de arquivos de áudio criados automaticamente e logs sem conteúdo de áudio |

Executar no Windows 11 x64 suportado. Registrar separadamente resultados no Windows 10 local e sua elegibilidade de suporte. Cobertura por dispositivo deve incluir USB, analógico e Bluetooth disponíveis. HDMI/DP, virtual e hardware adicional permanecem pendentes até ensaio próprio. Anotar captura bloqueada por privacidade do Windows, modo exclusivo e controles ausentes.

## Prova externa das Fases 2–3

1. Documentar integração virtual candidata, versão, licença, assinatura, instalação autorizada, restauração e desinstalação. Manter proteções do Windows habilitadas.
2. Direcionar navegador à entrada virtual, processar ganho conhecido no motor e reproduzir no endpoint físico. Comparar bypass/processado com sinal conhecido e verificar ausência de reprodução duplicada.
3. Capturar microfone físico, aplicar processamento mensurável e entregar ao endpoint de captura virtual selecionado em OBS e Discord. Confirmar sinal recebido no aplicativo externo.
4. Medir latência ponta a ponta com método descrito, além de CPU/RAM, taxa, buffers, underruns/overruns e recuperação após hotplug. Separar medição de estimativa.
5. Testar loops/mix-minus, destinos externos opt-in e restauração após encerramento normal e inesperado. Desinstalar a integração de teste quando autorizado e conferir estado anterior.

Registrar aprovação ou bloqueio de cada direção antes de ampliar o catálogo de efeitos. Testes DSP posteriores usarão senos, impulsos, silêncio, ruído, clipping, taxas e canais distintos, com comparação objetiva de ganho/resposta, estabilidade e latência.
