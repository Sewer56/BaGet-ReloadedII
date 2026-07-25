using System.Collections.Generic;
using BaGetter.Protocol.Models;

namespace BaGetter.Core;

public interface ISearchResponseBuilder
{
    SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> results);
    SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> results, long totalHits);
    AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data);
    AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data, long totalHits);
    DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> results);
}
