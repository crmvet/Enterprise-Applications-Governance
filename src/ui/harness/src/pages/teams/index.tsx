// Teams and committees (wave 4; the product owner asked for a page of the teams, committees and people who own the
// apps and data, to demo). The reads are list_teams (tool 95) and
// get_team (tool 96), document 24 B24-20. The shell imports the pages from here, and other pages import the link
// pieces from here.
//   /app/teams?kind=&q=&role=   TeamsList.tsx: one tab per kind with its count (counts follow the search and the role,
//                               never the tab), the search box (list_teams text), the role filter other pages link with;
//                               each row its kinds, roles, people, what waits on it, applications and owner gaps, each
//                               figure opening the team page at its rows. A table from 1024 px, cards below.
//   /app/teams/<objectId>       TeamPage.tsx: header with kinds, directory state and what it holds; six figures; Members
//   (?at=<section>)             (each to /app/people/<objectId>; Entra off, Entra not answering, demo personas); Decisions
//                               it answers (to the decision log by type); Roles and what they decide (rights, approvals,
//                               scopes, guideline chips); Waiting now (each to the decision page: a person answers
//                               there, D-38); Applications and owner roles; Owner gaps (to Owners); Data it owns;
//                               Recent answers
// Link pieces other pages render (links.tsx). Their props are the contract; the teams group builds their insides:
//   TeamLink           a group's name as a link to its team page (Roles and access binding groups, any group name)
//   HolderLink         a holder's name: a group to its team page, a person to the person page
//   OwnerHolderName    an owner's name on Application 360: HolderLink
//   WaitingOnTeams     on the decision page, under a pending request: "Waiting on the Change board" (list_teams
//                      roleKey), several teams each linked, or the owner role's holder and deputy
// The person page's "Member of" (list_teams memberObjectId), Owners by role's teams and the Owners views' search are
// built in Owners.tsx; people.tsx PersonLink sends a group to its team page.
export { TeamsPage } from './TeamsList';
export { TeamPage } from './TeamPage';
export { HolderLink, OwnerHolderName, TeamLink, WaitingOnTeams } from './links';
