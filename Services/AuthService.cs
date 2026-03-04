using Microsoft.AspNetCore.Identity;
using RubacCore.Dtos;
using RubacCore.Interfaces;
using RubacCore.Models;

namespace RubacCore.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
    }

    public async Task<bool> ValidateCredentialsAsync(string userName, string password)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || !user.IsActive) return false;

        var result = await _signInManager.CheckPasswordSignInAsync(
            user, password, lockoutOnFailure: true);

        return result.Succeeded;
    }

    public async Task<IEnumerable<string>> GetUserRolesAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        return user is null ? [] : await _userManager.GetRolesAsync(user);
    }

    public async Task<UserDto?> GetUserByNameAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto(user.Id, user.UserName!, user.Email!,
                           user.FirstName, user.LastName, user.IsActive, roles);
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto(user.Id, user.UserName!, user.Email!,
                           user.FirstName, user.LastName, user.IsActive, roles);
    }
}
