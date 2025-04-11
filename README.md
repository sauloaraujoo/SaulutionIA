# SalutionIA

Este é um projeto de prova de conceito (POC) desenvolvido em .NET com o objetivo de consumir APIs de inteligência artificial (ChatGPT e DeepSeek) para a **verificação e classificação automática de documentos**, como CNH, RG, certidões, entre outros.

## ⚙️ Tecnologias Utilizadas

- ASP.NET Core
- C#
- API da OpenAI (ChatGPT Vision)
- API da DeepSeek
- Git
- Visual Studio

## 🚀 Funcionalidades

- Upload de arquivos em diversos formatos (PDF, imagens, etc.)
- Análise de documentos utilizando IA
- Classificação automática do tipo de documento
- Estrutura modular com Controllers, Services e integração com modelos de IA

## 🛠️ Estrutura do Projeto

```
/Controllers      # Endpoints da API
/Models           # Modelos de dados
/Services         # Lógica de negócio e chamadas às APIs externas
/Swagger          # Documentação interativa da API
appsettings.json  # Configurações da aplicação
```

## ▶️ Como Executar

1. Clone o repositório:
   ```bash
   git clone https://github.com/sauloaraujoo/SaulutionIA.git
   ```

2. Restaure os pacotes:
   ```bash
   dotnet restore
   ```

3. Execute a aplicação:
   ```bash
   dotnet run
   ```

4. Acesse a documentação Swagger:
   ```
   https://localhost:<porta>/swagger
   ```

## 📂 Exemplo de Uso

Faça uma requisição `POST` com um arquivo para o endpoint `/api/documentos/classificar` e a IA retornará o tipo de documento identificado.
