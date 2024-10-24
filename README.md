# MTT AI Readiness CoHack Challenge: Unleash the Hidden Treasure

## Introduction

In this challenge-based hackathon, we will work together to build a working web app that uses Azure AI to reveal the treasure hidden in the image.

## Choose Your Language

- [C#](https://github.com/mttcohack/AI-Readiness-CoHack-1-Hackers-Unleash-the-Hidden-Treasure-with-Azure-AI/tree/main/C%23)
- [Python](https://github.com/mttcohack/AI-Readiness-CoHack-1-Hackers-Unleash-the-Hidden-Treasure-with-Azure-AI/tree/main/Python)

## Infrastructure Setup
To run the web app in your own environment, follow the setup steps below:
-	Create a Service Principal and secret
-	Create an Azure OpenAI resource
-	Create an Azure AI services multi-service account
-	Create an Azure Storage account
    -	Create a container, upload the image files
    -	Assign "Storage Blob Data Reader" role to the service principal
-	Create an Azure Key Vault resource
    - Create secrets `AOAI-KEY` and `COGNITIVE-KEY` with the key values of the Azure OpenAI resource and Azure AI services multi-service account
    - Assign "Key Vault Secrets User" role to the service principal
- Update the `appsettings.json` or `.env file` accordingly
