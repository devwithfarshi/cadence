namespace Cadence.Application.Common.Mapping;

// Cadence does not define its own IMapFrom marker — Mapster already ships one, and shadowing the
// name in a namespace imported alongside `using Mapster;` produces CS0104 at every use site (the
// same trap as ICommand/IBaseCommand; see Messaging/ICacheableQuery.cs).
//
// Mappings that need configuration implement Mapster's `IRegister`, placed in the module folder
// next to the DTOs they describe:
//
//     public sealed class MeetingMappings : IRegister
//     {
//         public void Register(TypeAdapterConfig config) =>
//             config.NewConfig<Meeting, MeetingSummaryDto>()
//                   .Map(dest => dest.ParticipantCount, src => src.Participants.Count);
//     }
//
// `AddApplication()` calls `config.Scan(assembly)`, which finds every IRegister — so a new mapping
// needs no registration. Identically-named members map on their own; restating those is noise that
// goes stale the moment a property is renamed.
//
// This file is documentation with a compile check: it lives in the assembly Scan() walks, so the
// convention it describes stays discoverable from the code rather than only from the blueprint.
internal static class MappingConventions
{
    /// <summary>Where module mappings belong, for anyone reading this file first.</summary>
    internal const string Location = "Application/Modules/<Module>/Mappings.cs";
}
