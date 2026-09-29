namespace ServiceOps.Domain.WorkOrders;

public enum Priority { Critical, High, Normal, Low }
public enum ServiceType { HVAC, Electrical, Plumbing, Equipment, GeneralMaintenance }
public enum WorkOrderStatus { New }
public enum SlaState { Good, AtRisk, Breached }
public enum WorkOrderEventType { Created }
