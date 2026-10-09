# SysDVR UWP (Xbox Series X|S Dev Mode)

Port do cliente **SysDVR** para a plataforma **UWP (Universal Windows Platform)**, otimizado para streaming de baixa latência do Nintendo Switch via rede local (TCP) diretamente no **Xbox Series X / S em Developer Mode**.

---

## 🚀 Como Compilar e Executar no Xbox

### 1. Pré-requisitos
- **Windows 10/11** com **Visual Studio 2022** (com a carga de trabalho *"Desenvolvimento com a Plataforma Universal do Windows"* e Windows 10 SDK 10.0.19041+).
- **Git** e **PowerShell**.
- **Xbox Series X ou S** com **Dev Mode** ativado e na mesma rede local que o PC e o Switch.

### 2. Configurar Dependências Nativas (SDL2 + FFmpeg x64-uwp)
No diretório deste projeto, execute o script automatizado no PowerShell:
```powershell
.\Setup-NativeDependencies.ps1
```
Esse script baixa e compila as versões compatíveis com o container UWP do SDL2 e FFmpeg via `vcpkg`, copiando as DLLs para `NativeLibs\x64\`.

### 3. Compilar no Visual Studio
1. Abra `SysDVR.UWP.sln` no Visual Studio 2022.
2. Defina a configuração como **Release** e a plataforma como **x64**.
3. Restaure os pacotes NuGet se solicitado (`Microsoft.NETCore.UniversalWindowsPlatform`, `System.Text.Json`).
4. Clique em **Build > Build Solution** (Compilar Solução).

### 4. Deploy no Xbox Series
#### Opção A — Direto pelo Visual Studio (Depuração / Deploy Remoto)
1. Nas propriedades do projeto > aba **Depurar (Debug)**:
   - Dispositivo de destino: **Computador Remoto (Remote Machine)**.
   - Endereço: IP do seu Xbox no Dev Mode (ex: `192.168.1.50`).
   - Modo de autenticação: **Universal (Não criptografado)**.
2. Pressione **F5** ou **Ctrl+F5** para instalar e iniciar no console.

#### Opção B — Via Xbox Device Portal (Navegador)
1. No Visual Studio: clique com o botão direito no projeto `SysDVR.UWP` > **Publicar** > **Criar Pacotes de Aplicativo**.
2. Selecione **Sideloading** > gere o pacote `.appx` para arquitetura `x64`.
3. No PC, abra o navegador e acesse a URL do Xbox Device Portal: `https://<IP_DO_XBOX>:11443`.
4. Vá em **My games & apps** > **Add** e envie o arquivo `.appx` gerado.
5. No Xbox, certifique-se de configurar o tipo do aplicativo como **Game** no Dev Home para liberar 5GB de RAM e 100% da GPU.

### 5. Utilização
1. No Switch: execute o sysmodule do SysDVR no modo TCP (padrão nas versões 6.0+).
2. No Xbox: abra o app **SysDVR**, informe o IP local do Nintendo Switch e clique em **Conectar (TCP)**.
3. O stream H.264 iniciará com decodificação por hardware/FFmpeg e renderização Direct3D 11 via SDL2.
