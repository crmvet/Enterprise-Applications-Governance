// The data records and the data parts of existing pages (D-37; docs/architecture/data-taxonomy.md):
//   /app/records/data-domain/<key>     DataDomainRecord.tsx: the path, the owners chain with every level up and the
//                                      level that answers marked, the rules with their source and the configuration
//                                      version, mappings by location, applications using it with state, decisions,
//                                      Fabric and Purview matches with "checked on", findings, the domains below, the
//                                      versions that changed it. Actions: propose an owner, add a mapping, move its rows
//   /app/records/data-location/<id>    DataLocationRecord.tsx: platform, stage, parent and children, technical owner
//                                      (own or from the parent), deployment target, hosting application, domains held
//                                      with origin and containers, last seen, flags, findings. Actions: confirm seen,
//                                      map data, change, retire, add a location inside it, name its technical owner
//   /app/records/data-mapping/<id>     DataMappingRecord.tsx: domain, location, containers, origin, copied from, copy
//                                      state with the copy rule and its decision. Actions: change, end, propose the copy
//                                      decision, withdraw a draft
//   /app/records/data-use/<id>         DataUseRecord.tsx: the application, domain path, location, access, purpose, AI
//                                      flag and service, de-identified, the state with "1 of 2 approvals", the governing
//                                      rule and where it is set in the engine's words, who is or would be asked, the
//                                      decisions and their approvals, the AI mark "Drafted by X for Y". Actions: confirm
//                                      an AI draft, change, end, propose or send its decision, withdraw a draft
//   App360DataPanel                    App360DataPanel.tsx, at the top of Application 360's Data section: the summary
//                                      line, banners for DC-09, DC-10 and DC-11, the uses with state, the derived data
//                                      owners, where it reaches the data, retention while it retires. Actions: declare a
//                                      use, confirm an AI draft, end a use, map the data, propose retention
//   WorkDataSection                    WorkDataSection.tsx, in My work: data approvals asked of the person, the domains
//                                      they own, deputize or steward with the inherited ones, the gaps in them, the
//                                      applications that send their data to AI
//   DecisionDataPanel                  DecisionDataPanel.tsx, on the decision page of a data decision: domain path,
//                                      classification, the use or the copy, the governing rule and its source, AI and
//                                      de-identified flags, the state with "1 of 2 approvals", who is asked, the version
// A use and a mapping have no read by ID: their pages find them in their domain (reads.ts useRowInDomain). Every link
// from these pages names the domain (?domain=<key>), so the page reads once; a link without it traces the domain first.
// Writes go through the data write sheets of ../data/sheets.tsx (imported, never copied), and submit_record and
// withdraw_record through the shared sheets of ../work-approvals/parts.tsx.
//
// The My work tab count stays approvals plus the queue sections: the Data section's gaps show but are not counted.
export { App360DataPanel } from './App360DataPanel';
export { DataDomainRecord } from './DataDomainRecord';
export { DataLocationRecord } from './DataLocationRecord';
export { DataMappingRecord } from './DataMappingRecord';
export { DataUseRecord } from './DataUseRecord';
export { DecisionDataPanel } from './DecisionDataPanel';
export { WorkDataSection } from './WorkDataSection';
