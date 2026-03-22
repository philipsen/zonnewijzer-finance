<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

# Finance App - Development Instructions

## Project Overview
This is a full-stack web application with C# ASP.NET Core backend and Vue.js frontend, featuring Google OAuth authentication and SQLite database.

## Project Structure
- **Backend/**: ASP.NET Core Web API (port 5000/5001)
- **Frontend/**: Vue.js application (port 3000)
- **Database**: SQLite (finance.db)

## Development Tasks Completed
✅ Created project structure
✅ Set up Google OAuth authentication
✅ Configured SQLite database with Entity Framework Core
✅ Implemented JWT token authentication
✅ Created Vue.js frontend with Google Sign-In
✅ Added CORS configuration for frontend/backend communication
✅ Set up development tasks for running both projects
✅ Created comprehensive documentation

## Configuration Required
1. **Google OAuth Setup**:
   - Update `appsettings.json` with your Google Client ID
   - Update `Frontend/src/App.vue` with your Google Client ID

2. **JWT Security**:
   - Change the JWT key in production

## Development Commands
- Backend: `dotnet run --project Backend/Backend.csproj`
- Frontend: `cd Frontend && npm run dev`
- Both: Use VS Code Task "Start Both (Backend + Frontend)"

## File Organization
- Keep authentication logic in `Backend/Controllers/AuthController.cs`
- Database models in `Backend/Models/`
- Vue components in `Frontend/src/components/`
- API services in `Backend/Services/`