using MudBlazor;

namespace Placebook.Models;

/// <summary>Übersetzt die Icon-Namen aus <c>categories.json</c> (Material-Icon-Namen) in MudBlazor-Icons.</summary>
public static class CategoryIcons
{
    public static string For(Category category) => category.Icon switch
    {
        "fastfood" => Icons.Material.Filled.Fastfood,
        "lunch_dining" => Icons.Material.Filled.LunchDining,
        "chair" => Icons.Material.Filled.Chair,
        "local_cafe" => Icons.Material.Filled.LocalCafe,
        "local_hospital" => Icons.Material.Filled.LocalHospital,
        "school" => Icons.Material.Filled.School,
        _ => Icons.Material.Filled.Place
    };
}
