# Como contribuir com o Wave Studio

Obrigado pelo interesse em contribuir com o **Wave Studio**! Este guia resume o fluxo prático e as regras essenciais do repositório (detalhadas em [`AGENTS.md`](AGENTS.md) e [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md)).

## Pré-requisitos

- **Windows 11 x64** (alvo prioritário) ou **Windows 10 x64** (experimental)
- SDK do .NET na versão definida em [`global.json`](global.json)

## Fluxo recomendado

1. Faça um fork e crie uma branch a partir da `main` atualizada.
2. Mantenha o PR pequeno e focado em uma única mudança.
3. Antes de abrir o PR, valide localmente (em Windows x64):

```powershell
dotnet restore WaveStudio.slnx --locked-mode
dotnet build WaveStudio.slnx -c Debug --no-restore -warnaserror
dotnet build WaveStudio.slnx -c Release --no-restore -warnaserror
dotnet test --solution WaveStudio.slnx -c Debug --no-build -- --culture pt-BR --explicit off
```

> **Importante:** não execute testes marcados como `Explicit` (hardware físico/áudio real) sem consentimento explícito.

## Regras importantes do projeto

- **Idioma dos comentários:** todo comentário explicativo em código, testes, scripts e documentação XML deve ser escrito em **português brasileiro (`pt-BR`)**, preservando identificadores/APIs e códigos de diagnóstico.
- **Traduções da interface:** textos visíveis na UI devem ser extraídos para recursos e mantidos completos em **PT-BR, EN e ES** (`StudioStrings*.resx`).
- **Transparência técnica:** nunca exponha controles fictícios ou funcionalidades ainda não implementadas como se estivessem prontas.
- **Privacidade e segurança:** processamento 100% local, sem telemetria de áudio, sem segredos no Git e sem instalação silenciosa de drivers ou alterações de sistema.
- **Documentação:** quando alterar comportamento, fases ou status, atualize `PROJECT_STATUS.md`, `CHANGELOG.md` ou `docs/` conforme aplicável.