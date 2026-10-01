namespace ServiceOps.Domain.WorkOrders;

public enum Priority { Critical, High, Normal, Low }
public enum ServiceType { HVAC, Electrical, Plumbing, Equipment, GeneralMaintenance }
public enum WorkOrderStatus { New, Assigned, InProgress, OnHold, Completed, Cancelled }
public enum SlaState { Good, AtRisk, Breached }
public enum WorkOrderEventType { Created, Assigned, Reassigned, Unassigned, DetailsCorrected, Started, PlacedOnHold, Resumed, Completed, Cancelled }
