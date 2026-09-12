namespace ChoNaBojo.Contracts.Consts;

public static class EventConflictCodes
{
	public const string VenueNotFound = "venue_not_found";
	public const string SportNotFound = "sport_not_found";
	public const string SportNotSupportedAtVenue = "sport_not_supported_at_venue";
	public const string EventNotFound = "event_not_found";
	public const string EventEnded = "event_ended";
	public const string EventCancelled = "event_cancelled";
	public const string EventFull = "event_full";
	public const string OrganizerCannotJoin = "organizer_cannot_join";
	public const string ParticipantNotAccepted = "participant_not_accepted";
	public const string RequestAlreadyResolved = "request_already_resolved";
	public const string RequestNotFound = "request_not_found";
}
