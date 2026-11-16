import Foundation

enum ReadableReport {
    static func sandbox(_ result:JSON) -> String {
        if let output=result["output"] as? String {
            return "TASK OUTPUT\n\n"+output+"\n\n"+verification(result)
        }
        if let passed=result["passed"] as? Bool {
            return passed ? "COPY VERIFIED\n\nNo supplied private terms or supported risks were found. Read the whole copy before sharing it.\n\n\((result["files"] as? [Any] ?? []).count) file(s) checked." : "REVIEW NEEDED\n\nThis copy did not pass verification. Execution and export stay paused. Open technical details for the full report."
        }
        if let steps=result["steps"] as? [JSON] { return "LOCAL AI TASK\n\n\(steps.count) step(s) recorded.\n"+verification(result)+"\n\nOpen technical details to inspect every action and tool result." }
        return "Your prepared copy is ready. Choose it in the list, verify it, then select a task to run."
    }
    private static func verification(_ result:JSON) -> String {
        (result["verification"] as? JSON)?["passed"] as? Bool == true ? "The prepared copy passed its final verification." : "Check the verification report before using or sharing these results."
    }
    static func tools(_ result:JSON) -> String {
        let docker=result["docker"] as? JSON ?? [:]
        return "Sandbox: \(docker["message"] as? String ?? "Not available")\nLocal AI: \(result["model_status"] as? String ?? "Not available")"
    }
    static func activity(_ result:JSON) -> String {
        let events=result["events"] as? [JSON] ?? []
        guard !events.isEmpty else { return "NOTHING HERE YET\n\nCompleted reviews and exports will appear here. Your prompts are not saved in this history." }
        let iso=ISO8601DateFormatter(),formatter=DateFormatter();formatter.dateStyle = .medium;formatter.timeStyle = .short
        return events.map { event in
            let value=event["at"] as? String ?? ""
            let time=iso.date(from:value).map { formatter.string(from:$0) } ?? value
            let name=(event["event"] as? String ?? "Operation").replacingOccurrences(of:"_",with:" ")
            let details=(event["details"] as? JSON ?? [:]).keys.sorted().map { key in "  "+key.replacingOccurrences(of:"_",with:" ")+": "+String(describing:(event["details"] as? JSON)?[key] ?? "") }.joined(separator:"\n")
            return time+"  ·  "+name+"\n"+details
        }.joined(separator:"\n\n")
    }
}
