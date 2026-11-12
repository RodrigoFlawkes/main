# Plano de testes

Estado em 12/11/2026 — planejamento. A execução dos ensaios de produto depende da implementação.

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
