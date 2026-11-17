using FoodRatingApp.Model;

namespace FoodRatingApp.Services;

public interface IFsaClient
{
    /// <summary>
    /// Gets one page of authorities, ordered by name. Only that page is fetched from the
    /// FSA API, so the full authority list is never downloaded.
    /// </summary>
    Task<FsaAuthorityList> GetAuthoritiesPage(int pageNumber, int pageSize);

    /// <summary>Gets a single authority, or <c>null</c> if the FSA does not know it.</summary>
    Task<FsaAuthority?> GetAuthority(int authorityId);

    /// <summary>Gets every establishment in an authority, across all FSA result pages.</summary>
    Task<IReadOnlyList<FsaEstablishment>> GetEstablishments(int authorityId);
}
