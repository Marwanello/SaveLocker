// Wire types for the dashboard. These are thin aliases over the contract generated
// from the server's OpenAPI document (see api-types.ts) so they can never drift from
// the C# DTOs. Regenerate with `npm run gen:api` after changing the server API.
//
// NonNullable<> strips the `| null` that .NET's OpenAPI attaches to a DTO's base schema
// when the type also appears in a nullable position elsewhere; nullability at each use
// site is expressed by the containing schema (e.g. GameStateDto.head is itself nullable).
import type { components } from './api-types';

type Schemas = components['schemas'];

export type GameSummary = Schemas['GameStateDto'];
export type Game = Schemas['GameDto'];
export type Version = NonNullable<Schemas['SaveVersionDto']>;
export type Lease = NonNullable<Schemas['LeaseDto']>;
export type Machine = Schemas['MachineDto'];
export type Command = Schemas['AgentCommandDto'];
export type Conflict = NonNullable<Schemas['ConflictDto']>;
export type VersionStats = NonNullable<Schemas['VersionStatsDto']>;
export type ExcludesPreview = NonNullable<Schemas['ExcludesPreviewDto']>;
export type BulkEnqueueResponse = NonNullable<Schemas['BulkEnqueueResponse']>;
export type CancelCommandsResponse = NonNullable<Schemas['CancelCommandsResponse']>;
export type Settings = Schemas['ServerSettingsDto'];
export type AppearanceSettings = Schemas['AppearanceSettingsDto'];
export type SetAppearanceRequest = Schemas['SetAppearanceRequest'];
export type MachineSavePath = Schemas['MachineSavePathDto'];
export type MachineScanCandidate = Schemas['MachineScanCandidateDto'];
export type AuditEntry = Schemas['AuditEntryDto'];
export type AgentHealth = Schemas['AgentHealthDto'];
export type AgentEvent = Schemas['AgentEventDto'];
export type Enrollment = Schemas['EnrollmentDto'];
export type EnrollmentPolicy = Schemas['EnrollmentPolicy'];
export type CreateEnrollmentResponse = Schemas['CreateEnrollmentResponse'];
export type EffectiveServerUrl = Schemas['EffectiveServerUrl'];
export type AdminStatus = Schemas['AdminStatus'];
export type ServerBuildInfo = NonNullable<Schemas['ServerBuildInfo']>;
export type AgentInstallerStatus = Schemas['AgentInstallerStatus'];
export type InstallerHashVerification = Schemas['InstallerHashVerification'];
export type AutoFetchSchedule = NonNullable<Schemas['AutoFetchSchedule']>;
export type ArtOptionsPage = Schemas['ArtOptionsPageDto'];
export type BackupInfo = NonNullable<Schemas['BackupInfo']>;
export type BackupStatus = Schemas['BackupStatusDto'];
export type BackupResult = Schemas['BackupResult'];
export type BackupRestoreResult = Schemas['BackupRestoreResult'];
export type SetBackupSettingsRequest = Schemas['SetBackupSettingsRequest'];
export type BackupDownloadTicket = Schemas['BackupDownloadTicket'];
export type ArtOption = NonNullable<Schemas['ArtOptionDto']>;

/**
 * Where a link into one game should land — a notification's action or the top bar's conflict pill:
 * `resolve` opens that conflict's panel already expanded (the game's first open one when the link does
 * not name one — an agent's event knows the game, not the conflict), `folder` puts that machine's
 * save-folder field into edit mode and focuses it. No intent just opens the game.
 */
export type GameIntent = { kind: 'resolve'; conflictId?: string } | { kind: 'folder'; machineId: string };

/** The two pieces of art a person can choose: the box-art cover and the square icon. */
export type ArtKind = 'grid' | 'icon';

/**
 * Which agent a hosted package is for. Hand-written because the server's `AgentPlatform` is a
 * vocabulary of wire constants rather than an enum, so it has no schema of its own — but these
 * strings are exactly what `?platform=` accepts, and an absent parameter means `win-x64`.
 */
export type AgentPlatform = 'win-x64' | 'linux-x64' | 'decky-plugin' | 'playnite-plugin';

/** A request to open one game — from a notification or the conflict pill — consumed once by GamesView.
 *  `seq` makes a second, identical request a real change, so it is honoured again. */
export interface GameRequest { id: string; intent?: GameIntent; seq: number }
