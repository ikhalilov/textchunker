# Incident response

When an alert fires, the on-call engineer acknowledges it within five minutes. The first step is to confirm customer impact and open an incident channel. Roles are assigned explicitly: commander, communications lead, and subject-matter expert.

Mitigation comes before root-cause analysis. Roll back the last deployment or shift traffic to a healthy region. Document every action with timestamps so the postmortem can reconstruct the timeline.

# Capacity planning

Forecast traffic from historical peaks and marketing events. Keep at least thirty percent headroom for failover and batch jobs. Autoscaling reacts to load, but it cannot create physical capacity instantly.

Database storage grows faster than request volume. Archive old partitions and monitor index bloat. Review quotas quarterly with product owners.

# Cost control

Tag every resource with team and environment. Budget alerts catch runaway clusters before the invoice arrives. Reserved instances reduce steady-state compute cost, while spot instances suit batch workloads.

Delete unattached disks and idle load balancers. Right-size instances using utilization metrics rather than guesses. Share savings reports with engineering leads every month.
