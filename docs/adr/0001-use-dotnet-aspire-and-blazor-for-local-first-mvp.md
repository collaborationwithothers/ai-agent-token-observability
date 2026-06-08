# Use .NET Aspire and Blazor for the Local-First MVP

We will build the Local-First MVP as a .NET Aspire application with a Blazor Local Dashboard, ASP.NET Core API, ingestion worker, and local PostgreSQL store. This keeps the local demo production-shaped, gives the app a single code-first orchestration model, and avoids splitting the implementation across Python Streamlit, Docker-only orchestration, or a separate JavaScript frontend.
