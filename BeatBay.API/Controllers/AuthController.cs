﻿using BeatBay.Data;
using BeatBay.DTOs;
using BeatBay.Model;
using BeatBay.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;

namespace BeatBay.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly BeatBayDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;

        public AuthController(
            BeatBayDbContext context,
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IEmailSender emailSender,
            IJwtService jwtService,
            IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _jwtService = jwtService;
            _configuration = configuration;
        }

        // **Registro de Usuario**
        [HttpPost("register")]
        public async Task<IActionResult> Register(CreateUserDto dto)
        {
            var user = new User
            {
                UserName = dto.UserName,
                Email = dto.Email,
                Name = dto.Name,
                Bio = dto.Bio,
                PlanId = dto.PlanId
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            // Enviar correo de confirmación
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmationLink = Url.Action("ConfirmEmail", "Auth", new { userId = user.Id, token = token }, Request.Scheme);

            var emailBody = $@"¡Bienvenido a BeatBay!

Hola {user.Name ?? user.UserName},

Gracias por registrarte en BeatBay. Para completar tu registro, por favor confirma tu email haciendo clic en el siguiente enlace:

{confirmationLink}

¡Gracias por unirte a nuestra comunidad musical!

El equipo de BeatBay";

            await _emailSender.SendEmailAsync(user.Email, "Confirma tu email - BeatBay", emailBody);

            await _userManager.AddToRoleAsync(user, "User");
            return Ok(new { message = "User created successfully. Please confirm your email." });
        }

        // **Confirmación de Email**
        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
                return BadRequest(new { message = "Email confirmation failed." });

            return Ok(new { message = "Email confirmed successfully." });
        }

        // **Login con JWT**
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var user = await _userManager.FindByNameAsync(dto.UserName);
            if (user == null)
                return Unauthorized(new { message = "Invalid credentials" });

            // Verificar si el email está confirmado
            if (!await _userManager.IsEmailConfirmedAsync(user))
                return Unauthorized(new { message = "Email not confirmed. Please check your email." });

            // Verificar si el usuario está activo
            if (!user.IsActive)
                return Unauthorized(new { message = "Account is deactivated. Contact support." });

            var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, false);
            if (!result.Succeeded)
                return Unauthorized(new { message = "Invalid credentials" });

            // Generar JWT token
            var jwtToken = await _jwtService.GenerateTokenAsync(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Calcular tiempo de expiración
            var expireMinutes = Convert.ToDouble(_configuration["Jwt:ExpireMinutes"]);
            var expiresAt = DateTime.UtcNow.AddMinutes(expireMinutes);

            // Crear DTO del usuario
            var userDto = new UserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Name = user.Name,
                Bio = user.Bio,
                PlanId = user.PlanId,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };

            // Respuesta con token
            var response = new AuthResponseDto
            {
                Token = jwtToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                User = userDto
            };

            return Ok(response);
        }

        // **Registro de Artista**
        [HttpPost("register-artist")]
        public async Task<IActionResult> RegisterArtist(CreateUserDto dto)
        {
            var user = new User
            {
                UserName = dto.UserName,
                Email = dto.Email,
                Name = dto.Name,
                Bio = dto.Bio,
                PlanId = dto.PlanId
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            // Enviar correo de confirmación para artista
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmationLink = Url.Action("ConfirmEmail", "Auth", new { userId = user.Id, token = token }, Request.Scheme);

            var emailBody = $@"¡Bienvenido a BeatBay como Artista!

Hola {user.Name ?? user.UserName},

Te has registrado como artista en BeatBay. Para completar tu registro, por favor confirma tu email:

{confirmationLink}

Una vez confirmado, podrás subir tu música y compartirla con la comunidad.

¡El equipo de BeatBay";

            await _emailSender.SendEmailAsync(user.Email, "Confirma tu email de Artista - BeatBay", emailBody);

            await _userManager.AddToRoleAsync(user, "Artist");

            return Ok(new { message = "Artist registered successfully. Please confirm your email." });
        }

        // **Validar Token**
        [HttpPost("validate-token")]
        public async Task<IActionResult> ValidateToken([FromBody] string token)
        {
            var principal = _jwtService.ValidateToken(token);
            if (principal == null)
                return Ok(new TokenValidationDto { IsValid = false, Message = "Invalid token" });

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Ok(new TokenValidationDto { IsValid = false, Message = "Invalid token claims" });

            var user = await _context.Users
                .Include(u => u.Plan)
                .FirstOrDefaultAsync(u => u.Id == int.Parse(userId));

            if (user == null || !user.IsActive)
                return Ok(new TokenValidationDto { IsValid = false, Message = "User not found or inactive" });

            var userDto = new UserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Name = user.Name,
                Bio = user.Bio,
                PlanId = user.PlanId,
                PlanName = user.Plan?.Name,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };

            return Ok(new TokenValidationDto
            {
                IsValid = true,
                Message = "Token is valid",
                User = userDto
            });
        }

        // **Refresh Token**
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken(RefreshTokenDto dto)
        {
            var principal = _jwtService.ValidateToken(dto.Token);
            if (principal == null)
                return BadRequest(new { message = "Invalid token" });

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null || !user.IsActive)
                return BadRequest(new { message = "User not found or inactive" });

            // Generar nuevo token
            var newJwtToken = await _jwtService.GenerateTokenAsync(user);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            var expireMinutes = Convert.ToDouble(_configuration["Jwt:ExpireMinutes"]);
            var expiresAt = DateTime.UtcNow.AddMinutes(expireMinutes);

            var response = new AuthResponseDto
            {
                Token = newJwtToken,
                RefreshToken = newRefreshToken,
                ExpiresAt = expiresAt,
                User = new UserDto
                {
                    Id = user.Id,
                    UserName = user.UserName,
                    Email = user.Email,
                    Name = user.Name,
                    Bio = user.Bio,
                    PlanId = user.PlanId,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt
                }
            };

            return Ok(response);
        }

        // **Recuperar Contraseña (Olvidó Contraseña)**
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
                return BadRequest(new { message = "Email not found" });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetLink = Url.Action("ResetPassword", "Auth", new { userId = user.Id, token = token }, Request.Scheme);

            var emailBody = $@"Restablecer Contraseña - BeatBay

Hola {user.Name ?? user.UserName},

Recibimos una solicitud para restablecer tu contraseña. Si no fuiste tú, puedes ignorar este email.

Para restablecer tu contraseña, copia y pega el siguiente enlace en tu navegador:

{resetLink}

Este enlace expirará en 24 horas por seguridad.

El equipo de BeatBay";

            // Enviar correo con enlace para resetear la contraseña
            await _emailSender.SendEmailAsync(user.Email, "Restablecer Contraseña - BeatBay", emailBody);

            return Ok(new { message = "Password reset link sent to email" });
        }

        // **Resetear Contraseña**
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
        {
            var user = await _userManager.FindByIdAsync(dto.UserId);
            if (user == null)
                return NotFound();

            var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok(new { message = "Password reset successfully" });
        }

        // **Logout**
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return Ok(new { message = "Logged out successfully" });
        }

        // **Obtener perfil del usuario actual**
        [HttpGet("profile")]
        [Authorize]
        public async Task<ActionResult<UserDto>> GetProfile()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var user = await _context.Users
                .Include(u => u.Plan)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound();

            var userDto = new UserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Name = user.Name,
                Bio = user.Bio,
                PlanId = user.PlanId,
                PlanName = user.Plan?.Name,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };

            return Ok(userDto);
        }

        // **Actualizar Usuario (Solo el propio usuario o admin puede modificar)**
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateUser(int id, UpdateUserDto dto)
        {
            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var isAdmin = User.IsInRole("Admin");

            if (id != currentUserId && !isAdmin)
                return Forbid();

            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            if (dto.Name != null) user.Name = dto.Name;
            if (dto.Bio != null) user.Bio = dto.Bio;
            if (dto.PlanId.HasValue) user.PlanId = dto.PlanId;

            await _context.SaveChangesAsync();
            return Ok(new { message = "User updated successfully" });
        }

        // **Obtener Usuario (Solo admin puede ver todos los usuarios)**
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<UserDto>> GetUser(int id)
        {
            var user = await _context.Users
                .Include(u => u.Plan)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            var userDto = new UserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Name = user.Name,
                Bio = user.Bio,
                PlanId = user.PlanId,
                PlanName = user.Plan?.Name,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };

            return Ok(userDto);
        }

        // **Desactivar Usuario (Solo admin puede hacerlo)**
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            user.IsActive = false;
            await _context.SaveChangesAsync();

            // Log admin action
            var adminUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var log = new AdminActionLog
            {
                AdminUserId = adminUserId,
                ActionType = "Deactivate User",
                Description = $"Deactivated user: {user.UserName}"
            };
            _context.AdminActionLogs.Add(log);
            await _context.SaveChangesAsync();

            return Ok(new { message = "User deactivated successfully" });
        }
    }
}