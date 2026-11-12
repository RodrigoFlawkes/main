# Plano da v0.1.0 Alpha

Planejamento em 12/11/2026. A implementação começa pela Fase 1, em incrementos verificáveis. Escopo detalhado em [REQUIREMENTS.md](REQUIREMENTS.md).

O Incremento 1 foi publicado no commit 60238ea após aprovação provisória do proprietário. O Incremento 2 foi implementado e aprovado provisoriamente para publicação e revisão técnica, com volume/mute/balanço, sincronização externa por polling, proteção de seleção e erros de suporte. Restore/build e 56 testes passaram no Windows 10 experimental, incluindo a regressão dos 16 anteriores. Leituras reais foram confirmadas. Testes físicos de escrita e avaliação visual permanecem pendentes. O Incremento 3 aguarda revisão técnica e configuração do fluxo ChatGPT/Codex pelo GitHub.

## Incrementos

F1-I3 executada conforme NEXT_TASK.md e REVIEW.md, com mixer compilado, 77 testes aprovados e sessões reais enumeradas. O critério físico de dois aplicativos permanece pendente. Incremento 4 aguarda nova orientação. As etapas abaixo mantêm a sequência do roadmap.

1. Criar `WaveStudio.slnx`, Desktop, Audio e testes conforme a arquitetura. Fixar SDK e dependências após conferir APIs e runner. Abrir janela pt-BR, enumerar endpoints reais, mostrar estados e padrões e permitir seleção independente. Critério de saída — restore/build/test bem-sucedidos e comparação manual da enumeração com o Windows.
2. Adicionar volume/mute por endpoint e balanço condicional. Sincronizar mudanças externas, validar intervalos e erros de dispositivo. Critério — alterações verificadas no Windows e teste físico dos canais, incluindo mono e suporte ausente.
3. Listar sessões reais do endpoint, identidade, estado e nome/processo quando disponíveis. Adicionar volumes/mute e tratar sessões criadas/encerradas durante o uso. Critério — dois aplicativos independentes e sons do sistema controlados corretamente.
4. Adicionar medidores de pico reais, atualização moderada, notificações de hotplug/padrão e recuperação. Critério — reprodução e fala movimentam os medidores disponíveis, desconexão/reconexão e suspensão são tratadas sem travamento ou uso de objetos descartados.
5. Persistir configurações versionadas, preferências por endpoint e identidade segura para preferências de sessão quando disponível. Implementar backup, leitura tolerante a corrupção e restauração explícita de valores. Critério — reinício recupera seleções e preferências, preserva arquivo incompatível e informa falhas de escrita.
6. Executar a matriz de aceitação, corrigir erros e atualizar documentação com evidências. Gerar executável x64 `WaveStudio.exe` e instruções de execução. Critério — distinguir testes automáticos, físicos aprovados e limitações por dispositivo antes de identificar a versão como Alpha funcional.

Cada incremento inclui testes relevantes e atualização de status/changelog. Falha de suporte por endpoint deve ter explicação visível. Ganho digital, EQ, mesa, roteamento, soundboard e demais telas entram conforme as fases posteriores ganhem implementação.

## Verificação prevista após criar os projetos

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
