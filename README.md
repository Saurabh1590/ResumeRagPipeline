# Resume RAG Pipeline

A high-performance Minimal API built with **.NET 8** that implements a Retrieval-Augmented Generation (RAG) pipeline for processing and querying resume documents. 

## 🚀 Features
* **PDF Ingestion**: Parses raw text seamlessly from PDF resumes using `PdfPig`.
* **Deterministic Vector ID Generation**: Uses MD5 hashing of filenames to generate stable UUIDs, ensuring effortless document overwrites/updates in the vector database.
* **Vector Database Integration**: Seamless communication with `Qdrant` using native gRPC clients.
* **Azure OpenAI Integration**: Leverages `text-embedding-3-small` for semantic representation and `gpt-4o-mini` for context-aware HR conversational retrieval.

## 🛠️ Tech Stack
* **Backend**: .NET 8 (Minimal APIs)
* **Vector DB**: Qdrant (gRPC Client)
* **LLM & Embeddings**: Azure OpenAI SDK (`Azure.AI.OpenAI`)
* **PDF Extraction**: PdfPig

## ⚙️ Configuration Setup
To run this project locally, update your local User Secrets tool or update the placeholders in `appsettings.json`:
