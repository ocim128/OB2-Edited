using System.Security.Claims;
using AutoMapper;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Flux.Core.Models.Settings;
using Flux.Core.Services;
using Flux.Web.Auth;
using Flux.Web.Dtos.Settings;
using Flux.Web.Exceptions;
using Flux.Web.Interfaces;
using Flux.Web.Services;
using RuriLib.Functions.Captchas;
using RuriLib.Models.Settings;
using RuriLib.Services;

namespace Flux.Web.Controllers;

/// <summary>
/// Manage settings.
/// </summary>
[ApiVersion("1.0")]
public class SettingsController : ApiController
{
    private readonly IMapper _mapper;
    private readonly IAuthTokenService _authService;
    private readonly IConfiguration _configuration;
    private readonly FluxSettingsService _fluxSettingsService;
    private readonly RuriLibSettingsService _ruriLibSettingsService;
    private readonly ThemeService _themeService;
    private readonly ILogger<SettingsController> _logger;
    
    /// <summary></summary>
    public SettingsController(RuriLibSettingsService ruriLibSettingsService,
        FluxSettingsService fluxSettingsService, IMapper mapper,
        IAuthTokenService authService, IConfiguration configuration,
        ThemeService themeService, ILogger<SettingsController> logger)
    {
        _ruriLibSettingsService = ruriLibSettingsService;
        _fluxSettingsService = fluxSettingsService;
        _mapper = mapper;
        _authService = authService;
        _configuration = configuration;
        _themeService = themeService;
        _logger = logger;
    }
    
    /// <summary>
    /// Get the system settings.
    /// </summary>
    [TypeFilter<GuestFilter>]
    [HttpGet("system")]
    [MapToApiVersion("1.0")]
    public ActionResult<SystemSettingsDto> GetSystemSettings()
    {
        var systemSettings = new SystemSettingsDto();
        
        var botLimit = _configuration.GetSection("Resources")["BotLimit"];
        
        if (botLimit is not null)
        {
            systemSettings.BotLimit = int.Parse(botLimit);
        }
        
        return systemSettings;
    }

    /// <summary>
    /// Get the environment settings.
    /// </summary>
    [TypeFilter<GuestFilter>]
    [HttpGet("environment")]
    [MapToApiVersion("1.0")]
    public ActionResult<EnvironmentSettingsDto> GetEnvironmentSettings()
        => _mapper.Map<EnvironmentSettingsDto>(
            _ruriLibSettingsService.Environment);

    /// <summary>
    /// Get the RuriLib settings.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpGet("rurilib")]
    [MapToApiVersion("1.0")]
    public ActionResult<GlobalSettings> GetRuriLibSettings()
        => _ruriLibSettingsService.RuriLibSettings;

    /// <summary>
    /// Get the RuriLib default settings.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpGet("rurilib/default")]
    [MapToApiVersion("1.0")]
    public ActionResult<GlobalSettings> GetRuriLibDefaultSettings()
        => new GlobalSettings();

    /// <summary>
    /// Update the RuriLib settings.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpPut("rurilib")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<GlobalSettings>> UpdateRuriLibSettings(
        GlobalSettings settings)
    {
        // NOTE: We use the mapper here to apply the new settings over the
        // existing ones, so we don't update the references, and we can
        // edit the settings live if a component is using them.

        // NOTE: To check this we can just print the hashcode before
        // and after this instruction.
        _mapper.Map(settings, _ruriLibSettingsService.RuriLibSettings);
        await _ruriLibSettingsService.Save();
        
        _logger.LogInformation("Updated RuriLib settings");

        return _ruriLibSettingsService.RuriLibSettings;
    }

    /// <summary>
    /// Get the Flux settings.
    /// </summary>
    /// <returns></returns>
    [TypeFilter<AdminFilter>]
    [HttpGet]
    [MapToApiVersion("1.0")]
    public ActionResult<FluxSettingsDto> GetSettings()
        => _mapper.Map<FluxSettingsDto>(_fluxSettingsService.Settings);

    /// <summary>
    /// Get the default Flux settings.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpGet("default")]
    [MapToApiVersion("1.0")]
    public ActionResult<FluxSettingsDto> GetDefaultSettings()
        => _mapper.Map<FluxSettingsDto>(new FluxSettings());

    /// <summary>
    /// Get the safe Flux settings that even a guest user is allowed to see.
    /// </summary>
    [TypeFilter<GuestFilter>]
    [HttpGet("safe")]
    [MapToApiVersion("1.0")]
    public ActionResult<SafeFluxSettingsDto> GetSafeSettings()
        => _mapper.Map<SafeFluxSettingsDto>(_fluxSettingsService.Settings);

    /// <summary>
    /// Update the Flux settings.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpPut]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<FluxSettingsDto>> UpdateSettings(
        FluxSettingsDto settings)
    {
        // NOTE: We use the mapper here to apply the new settings over the
        // existing ones so we don't update the references and we can
        // edit the settings live if a component is using them.

        var oldAdminUsername = _fluxSettingsService.Settings.SecuritySettings.AdminUsername;
        var newAdminUsername = settings.SecuritySettings.AdminUsername;
        
        // NOTE: To check this we can just print the hashcodes before
        // and after this instruction.
        _mapper.Map(settings, _fluxSettingsService.Settings);
        await _fluxSettingsService.SaveAsync();
        
        _logger.LogInformation("Updated Flux settings");
        
        // If the admin username has changed, sign a new JWT and send it back
        if (oldAdminUsername != newAdminUsername)
        {
            var claims = new[] {
                new Claim(ClaimTypes.NameIdentifier, "0", ClaimValueTypes.Integer),
                new Claim(ClaimTypes.Name, newAdminUsername), new Claim(ClaimTypes.Role, "Admin")
            };

            var lifetimeHours = Math.Clamp(_fluxSettingsService.Settings.SecuritySettings.AdminSessionLifetimeHours, 0, 9999);
            var token = _authService.GenerateToken(claims, TimeSpan.FromHours(lifetimeHours));
            
            Response.Headers.Append("X-New-Jwt", token);
        }

        return _mapper.Map<FluxSettingsDto>(_fluxSettingsService.Settings);
    }

    /// <summary>
    /// Update the password of the admin user. Note that this does NOT
    /// invalidate the tokens that were granted so far.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpPatch("admin/password")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> UpdateAdminPassword(UpdateAdminPasswordDto dto,
        [FromServices] IValidator<UpdateAdminPasswordDto> validator)
    {
        await validator.ValidateAndThrowAsync(dto);
        
        _fluxSettingsService.Settings.SecuritySettings
            .SetupAdminPassword(dto.Password);
        await _fluxSettingsService.SaveAsync();
        
        _logger.LogInformation("Updated the password of the admin user");

        return Ok();
    }

    /// <summary>
    /// Add a CSS theme.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpPost("theme")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> AddTheme(IFormFile file)
    {
        await _themeService.SaveCssFileAsync(file.FileName, file.OpenReadStream());
        _logger.LogInformation("Added a new CSS theme from file {FileName}", file.FileName);
        return Ok();
    }

    /// <summary>
    /// Get all CSS themes.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpGet("theme/all")]
    [MapToApiVersion("1.0")]
    public ActionResult<List<ThemeDto>> GetAllThemes() =>
        _themeService.GetThemeNames()
            .Select(n => new ThemeDto { Name = n })
            .ToList();

    /// <summary>
    /// Get the file corresponding to a CSS theme.
    /// </summary>
    [HttpGet("theme")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> GetTheme(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = _fluxSettingsService.Settings.CustomizationSettings.Theme;
        }

        var bytes = Array.Empty<byte>();

        try
        {
            bytes = await _themeService.GetCssFileAsync(name);
        }
        catch
        {
            // If anything happens, return an empty file
        }

        return File(bytes, "text/css", "theme.css");
    }
    
    /// <summary>
    /// Get all custom snippets.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpGet("custom-snippets")]
    [MapToApiVersion("1.0")]
    public ActionResult<Dictionary<string, string>> GetCustomSnippets()
        => _fluxSettingsService.Settings.GeneralSettings.CustomSnippets
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToDictionary(s => s.Name, s => s.Body);
    
    /// <summary>
    /// Check captcha balance for the given service.
    /// </summary>
    [TypeFilter<AdminFilter>]
    [HttpPost("check-captcha-balance")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<CaptchaBalanceDto>> CheckCaptchaCredit(
        CaptchaSettings captchaSettings)
    {
        var service = CaptchaServiceFactory.GetService(captchaSettings);
        
        try
        {
            return new CaptchaBalanceDto
            {
                Balance = await service.GetBalanceAsync()
            };
        }
        catch (Exception ex)
        {
            throw new ApiException(ErrorCode.CaptchaServiceError,
                $"Error while checking the balance on {captchaSettings.CurrentService}: {ex.Message}");
        }
    }
}
