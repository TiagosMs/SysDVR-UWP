<div align="center">

# 🎮 SysDVR UWP — Xbox Series X|S

**Transmita gameplay do seu Nintendo Switch diretamente para o seu Xbox Series X / Series S em 720p 60FPS e com latência ultrabaixa via rede local — sem precisar de placa de captura!**

[![Latest Release](https://img.shields.io/github/v/release/TiagosMs/SysDVR-UWP?style=for-the-badge&color=107C41&label=Release)](https://github.com/TiagosMs/SysDVR-UWP/releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Xbox%20Series%20X%7CS%20(Dev%20Mode)-107C41?style=for-the-badge&logo=xbox)](https://github.com/TiagosMs/SysDVR-UWP)
[![Build Status](https://img.shields.io/github/actions/workflow/status/TiagosMs/SysDVR-UWP/build-uwp.yml?branch=main&style=for-the-badge&label=Build)](https://github.com/TiagosMs/SysDVR-UWP/actions)
[![License](https://img.shields.io/badge/License-GPL--2.0-blue.svg?style=for-the-badge)](LICENSE)

</div>

---

## 📖 Sobre o Projeto

O **SysDVR-UWP** é um cliente nativo universal (UWP) do projeto [SysDVR](https://github.com/exelix11/SysDVR), desenvolvido especificamente para o ecossistema **Xbox (Series X e Series S) em Developer Mode**.

Utilizando pipeline de aceleração com **FFmpeg 7** para decodificação H.264 e **SDL2** para reprodução de áudio sem buffer desnecessário, o aplicativo entrega uma experiência de streaming direto de videogame para videogame com latência perceptível menor que 30ms em conexões de rede locais estáveis.

---

## ✨ Funcionalidades Principais

* ⚡ **Latência Ultrabaixa (<30ms):** Pipeline de decodificação otimizado em tempo real com buffer de áudio de 1 pacote no modo baixa latência.
* 🔄 **Auto-Reconexão Inteligente (Sleep / Wake):** Se o Nintendo Switch entrar em suspensão ou a rede oscilar, o app não fecha: ele monitora a porta silenciosamente e restabelece a transmissão assim que o console acorda.
* 📊 **HUD em Tempo Real:** Contador no topo da tela informando **FPS** e **Bitrate (Mbps)** da transmissão.
* 🚀 **Socket TCP Otimizado:** Buffer de recebimento ampliado para 1 MB e `TCP_NODELAY` ativado para evitar perda de pacotes e travamentos em Wi-Fi.
* 🎮 **Navegação Nativa por Gamepad:** Interface moderna, limpa e pensada 100% para o controle do Xbox (suporte total a D-Pad, analógicos e atalhos rápidos).
* 🖥️ **Modo Tela Cheia Imersivo:** Oculte toda a interface e aproveite seu jogo na TV inteira com um clique no controle (`[X]`).
* 🛡️ **Diagnóstico de Erros do Switch:** Mensagens claras caso algum jogo impeça a gravação de tela através do serviço `grc:d` do sistema operacional do Switch.

---

## 🎮 Controles no Gamepad Xbox

| Botão | Ação |
| :--- | :--- |
| **`[A]`** | Conectar / Ativar opção selecionada |
| **`[B]`** | Desconectar / Cancelar auto-reconexão / Sair da tela cheia |
| **`[X]`** | Alternar Modo Tela Cheia (Fullscreen) |
| **`[Y]`** | Alternar exibição do HUD de FPS e Bitrate |
| **`[D-Pad]` / `[Analógico Esquerdo]`** | Navegar entre botões e campos de entrada |

---

## 📥 Guia de Instalação no Xbox (Passo a Passo)

### 📋 Pré-requisitos
1. Um **Nintendo Switch** desbloqueado com o sysmodule [SysDVR](https://github.com/exelix11/SysDVR/releases) instalado e configurado no modo de rede (TCP).
2. Um **Xbox Series X** ou **Xbox Series S** com o **Developer Mode** ativado.
3. Um PC ou smartphone conectado na mesma rede local que o console.

---

### Passo 1 — Baixar a Release
Acesse a página de lançamentos e baixe a versão estável mais recente:
👉 **[Baixar SysDVR-UWP na Página de Releases](https://github.com/TiagosMs/SysDVR-UWP/releases/latest)**

Baixe o arquivo compactado: **`SysDVR-UWP-Xbox-x64.zip`** e extraia-o no seu computador. Você encontrará:
* `SysDVR.UWP_x.x.x.x_x64.msix` (ou `.appx`): o pacote principal do aplicativo.
* `SysDVR_Dev.cer`: o certificado de segurança para instalação no Xbox.
* Pasta `Dependencies\x64`: dependências de runtime necessárias da Microsoft.

---

### Passo 2 — Acessar o Xbox Device Portal
1. No Xbox, abra o aplicativo **Dev Home**.
2. Na tela inicial ou em *Remote Access*, anote a URL informada (exemplo: `https://192.168.1.50:11443`).
3. Abra o navegador no seu PC e acesse esse endereço.
4. Caso o navegador exiba um aviso de segurança (certificado auto-assinado), clique em **Avançado** > **Continuar para o site**.

---

### Passo 3 — Instalar o Certificado (`SysDVR_Dev.cer`)
> [!IMPORTANT]
> Este passo só precisa ser feito uma única vez. Sem o certificado, o Xbox não permitirá a instalação do pacote.

1. No menu do Xbox Device Portal, clique na aba **Security** ou **Certificates** (ou durante a tela de instalação em *Install certificate*).
2. Clique em **Browse...** e selecione o arquivo **`SysDVR_Dev.cer`** que veio no arquivo ZIP.
3. Clique em **Install** e confirme.

---

### Passo 4 — Instalar o Aplicativo e as Dependências
1. No Xbox Device Portal, vá para **My games & apps** e clique no botão **Add**.
2. No campo **Choose package**, selecione o arquivo principal **`SysDVR.UWP_*.msix`** (ou `.appx`).
3. No campo **Choose dependencies**, selecione **TODOS** os arquivos contidos dentro da pasta `Dependencies\x64`:
   * `Microsoft.VCLibs.x64.14.00.appx`
   * `Microsoft.NET.CoreRuntime.2.2.appx`
   * `Microsoft.NET.CoreFramework.Debug.2.2.appx`
4. Clique em **Deploy** e aguarde a barra de progresso atingir 100%.

---

### Passo 5 — [IMPORTANTE] Mudar o Tipo de App para "Game"
> [!TIP]
> Essa configuração é essencial para garantir 60 FPS cravados sem engasgos do sistema.

1. No Xbox, volte para a tela do **Dev Home**.
2. Localize o **SysDVR** na lista de aplicativos instalados.
3. Pressione o botão **`[View]`** (botão dos dois quadrados no controle) ou vá em *More actions*.
4. Altere a opção **App type** de `App` para **`Game`**.
5. Quando executado como *Game*, o Xbox aloca núcleos dedicados da CPU e libera mais de 5 GB de memória de vídeo para o aplicativo.

---

### Passo 6 — Iniciar e Conectar
1. Certifique-se de que o Nintendo Switch está ligado e com o SysDVR rodando no mesmo Wi-Fi/rede.
2. Abra o **SysDVR** no Xbox.
3. Digite o endereço IP do seu Switch (o último IP usado fica salvo automaticamente).
4. Pressione **`[A]`** em **Conectar (TCP)** e divirta-se!

---

## ⚙️ Configuração no Nintendo Switch

Para transmitir via rede local no Switch:
1. Tenha o sysmodule `SysDVR` instalado em `sdmc:/atmosphere/contents/00FF0000A5366839/`.
2. O modo padrão de transmissão é TCP na porta `9911`.
3. Você pode usar o homebrew **SysDVR-Settings** no Switch para alternar o modo para **TCP Only** e ajustar os canais de áudio/vídeo conforme sua preferência.

---

## 🛠️ Compilando a partir do Código-Fonte

Se você deseja modificar ou compilar o projeto você mesmo:

### Requisitos:
* **Windows 10 / 11** com **Visual Studio 2022** (com as cargas de trabalho *Desenvolvimento com a Plataforma Universal do Windows* e *C++ para UWP*).
* **vcpkg** e **PowerShell**.

### Passos:
```powershell
# 1. Clone o repositório
git clone https://github.com/TiagosMs/SysDVR-UWP.git
cd SysDVR-UWP

# 2. Configure as dependências nativas (FFmpeg 7 e SDL2 para UWP x64)
.\Setup-NativeDependencies.ps1

# 3. Abra a solução no Visual Studio
SysDVR.UWP.sln
```
Compile a solução na configuração **Release | x64**.

---

## ❓ Perguntas Frequentes & Solução de Problemas

<details>
<summary><b>Aparece mensagem de "Erro grc:d" ou tela preta em certos momentos</b></summary>
O Nintendo Switch possui uma proteção do sistema operacional (`grc:d`) onde certos jogos ou telas de menu bloqueiam propositalmente a captura interna de vídeo. Nesses momentos, o Switch simplesmente não envia quadros. Assim que o jogo sair dessa cena restrita, o sinal retorna normalmente.
</details>

<details>
<summary><b>O Switch entrou em sleep mode e a imagem congelou</b></summary>
Com o recurso de **Auto-Reconexão** ativado, basta acordar o Switch no botão Power. O aplicativo no Xbox detectará o retorno do console em poucos segundos e restabelecerá o vídeo e o áudio sozinho.
</details>

<details>
<summary><b>Sinto pequenos engasgos ou perda de fluidez</b></summary>
- Certifique-se de configurar o SysDVR como **Game** no Xbox Dev Home (Passo 5 do guia).
- Para melhor desempenho de rede, utilize redes Wi-Fi de **5 GHz** ou conecte ambos os consoles via cabo Ethernet no mesmo roteador.
</details>

---

## 🤝 Créditos & Agradecimentos

* **[Tiago](https://github.com/TiagosMs)** — Desenvolvedor e mantenedor deste port UWP.
* **[exelix11](https://github.com/exelix11/SysDVR)** — Autor original do SysDVR.
* **[laurorual/sysdvr-webos](https://github.com/laurorual/sysdvr-webos)** — Referência e inspiração para otimizações de socket TCP e auto-reconexão.
* **Equipe FFmpeg & libsdl** — Pelas bibliotecas de multimídia essenciais.

---

## 📄 Licença

Este projeto é distribuído sob a licença **GNU General Public License v2.0 (GPL-2.0)**. Consulte o arquivo [LICENSE](LICENSE) para mais detalhes.
