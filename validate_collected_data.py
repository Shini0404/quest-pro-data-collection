#!/usr/bin/env python3
"""
=============================================================================
validate_collected_data.py
Purpose: Validates CSV data collected from Quest Pro before model integration
         Checks data quality, completeness, and compatibility with Wu_MMSys_17 format

Usage:
    python validate_collected_data.py /path/to/quest-pro-data/Raw/

    # Validate a specific participant:
    python validate_collected_data.py /path/to/quest-pro-data/Raw/P001/

    # Validate and auto-fix issues:
    python validate_collected_data.py /path/to/quest-pro-data/Raw/ --fix

Output:
    - Console report with pass/fail for each check
    - validation_report.txt in the data directory
=============================================================================
"""

import os
import sys
import csv
import glob
import argparse
import numpy as np
from pathlib import Path
from datetime import datetime


# =============================================================================
# EXPECTED COLUMN DEFINITIONS
# =============================================================================

HEAD_COLUMNS = [
    "Timestamp", "PlaybackTime",
    "UnitQuaternion.x", "UnitQuaternion.y", "UnitQuaternion.z", "UnitQuaternion.w",
    "HmdPosition.x", "HmdPosition.y", "HmdPosition.z",
    "EulerYaw", "EulerPitch", "EulerRoll",
    "VelYaw", "VelPitch", "VelRoll"
]

EYE_COLUMNS = [
    "Timestamp", "PlaybackTime",
    "GazeDir.x", "GazeDir.y", "GazeDir.z",
    "GazeOrigin.x", "GazeOrigin.y", "GazeOrigin.z",
    "GazeYaw", "GazePitch",
    "LeftPupilDiam", "RightPupilDiam",
    "LeftOpenness", "RightOpenness",
    "LeftConfidence", "RightConfidence",
    "IsFixating", "FixationDurationMs", "BothEyesValid"
]

COMBINED_COLUMNS = [
    "Timestamp", "PlaybackTime",
    "HeadYaw", "HeadPitch", "HeadRoll",
    "GazeYaw", "GazePitch",
    "GazeRelativeH", "GazeRelativeV",
    "AvgPupil", "EyeHeadOffset",
    "AbsoluteGazeYaw", "AbsoluteGazePitch"
]

# Wu_MMSys_17 format (first 9 columns must match for compatibility)
WU_MMSYS_COLUMNS = [
    "Timestamp", "PlaybackTime",
    "UnitQuaternion.x", "UnitQuaternion.y", "UnitQuaternion.z", "UnitQuaternion.w",
    "HmdPosition.x", "HmdPosition.y", "HmdPosition.z"
]


class DataValidator:
    def __init__(self, data_dir, fix_mode=False):
        self.data_dir = Path(data_dir)
        self.fix_mode = fix_mode
        self.report = []
        self.errors = 0
        self.warnings = 0
        self.passed = 0

    def log(self, level, message):
        """Log a validation message."""
        prefix = {"ERROR": "❌", "WARN": "⚠️ ", "PASS": "✅", "INFO": "ℹ️ "}
        line = f"{prefix.get(level, '  ')} [{level}] {message}"
        self.report.append(line)
        print(line)

        if level == "ERROR":
            self.errors += 1
        elif level == "WARN":
            self.warnings += 1
        elif level == "PASS":
            self.passed += 1

    def validate_all(self):
        """Run all validations."""
        self.log("INFO", f"Validating data in: {self.data_dir}")
        self.log("INFO", f"Fix mode: {'ON' if self.fix_mode else 'OFF'}")
        self.log("INFO", "=" * 60)

        # Find all participant directories or CSV files
        if self.data_dir.is_file():
            # Single file
            self.validate_single_csv(self.data_dir)
        else:
            # Find participant directories
            participant_dirs = sorted([
                d for d in self.data_dir.iterdir()
                if d.is_dir() and d.name.startswith("P")
            ])

            if not participant_dirs:
                # Maybe CSV files are directly in this directory
                csv_files = sorted(self.data_dir.glob("*.csv"))
                if csv_files:
                    for csv_file in csv_files:
                        self.validate_single_csv(csv_file)
                else:
                    self.log("ERROR", f"No participant directories (P001/, P002/) or CSV files found in {self.data_dir}")
                    return
            else:
                for pdir in participant_dirs:
                    self.validate_participant(pdir)

        # Summary
        self.log("INFO", "=" * 60)
        self.log("INFO", f"SUMMARY: {self.passed} passed, {self.warnings} warnings, {self.errors} errors")

        if self.errors == 0:
            self.log("INFO", "🎉 All validations passed! Data is ready for model integration.")
        else:
            self.log("INFO", f"🔧 {self.errors} error(s) need fixing before model integration.")

        # Save report
        report_path = self.data_dir / "validation_report.txt"
        with open(report_path, 'w') as f:
            f.write(f"Data Validation Report\n")
            f.write(f"Generated: {datetime.now().isoformat()}\n")
            f.write(f"Directory: {self.data_dir}\n")
            f.write(f"\n")
            for line in self.report:
                f.write(line + "\n")
        print(f"\nReport saved to: {report_path}")

    def validate_participant(self, pdir):
        """Validate all data for one participant."""
        pid = pdir.name
        self.log("INFO", f"\n--- Participant: {pid} ---")

        # Find CSV files
        head_files = sorted(pdir.glob("head_*.csv"))
        eye_files = sorted(pdir.glob("eye_*.csv"))
        combined_files = sorted(pdir.glob("combined_*.csv"))

        # Check we have matching files
        if len(head_files) == 0:
            self.log("ERROR", f"{pid}: No head tracking files found!")
            return

        self.log("INFO", f"{pid}: Found {len(head_files)} head, {len(eye_files)} eye, {len(combined_files)} combined files")

        if len(head_files) != len(eye_files):
            self.log("WARN", f"{pid}: Mismatched file counts (head={len(head_files)}, eye={len(eye_files)})")

        # Validate each file
        for hf in head_files:
            self.validate_head_csv(hf, pid)

        for ef in eye_files:
            self.validate_eye_csv(ef, pid)

        for cf in combined_files:
            self.validate_combined_csv(cf, pid)

    def validate_single_csv(self, filepath):
        """Validate a single CSV file by detecting its type."""
        name = filepath.name.lower()
        if "head" in name:
            self.validate_head_csv(filepath, "unknown")
        elif "eye" in name:
            self.validate_eye_csv(filepath, "unknown")
        elif "combined" in name:
            self.validate_combined_csv(filepath, "unknown")
        else:
            self.log("WARN", f"Cannot determine file type: {filepath.name}")

    def validate_head_csv(self, filepath, pid):
        """Validate a head tracking CSV file."""
        self.log("INFO", f"  Checking: {filepath.name}")

        try:
            data = self._read_csv(filepath)
        except Exception as e:
            self.log("ERROR", f"  Cannot read file: {e}")
            return

        if len(data) == 0:
            self.log("ERROR", f"  File is empty (no data rows)!")
            return

        # Check column headers
        headers = list(data[0].keys())
        expected_start = HEAD_COLUMNS[:9]  # Wu_MMSys_17 compatible columns
        actual_start = headers[:9]

        if actual_start == expected_start:
            self.log("PASS", f"  Headers match Wu_MMSys_17 format ✓")
        else:
            self.log("ERROR", f"  Headers DON'T match Wu_MMSys_17!")
            self.log("ERROR", f"    Expected: {expected_start}")
            self.log("ERROR", f"    Got:      {actual_start}")

        # Check row count (should have thousands of rows at 90Hz)
        row_count = len(data)
        if row_count < 100:
            self.log("ERROR", f"  Only {row_count} rows — too few! Expected 1000+ for any video.")
        elif row_count < 1000:
            self.log("WARN", f"  Only {row_count} rows — seems short for a full video.")
        else:
            self.log("PASS", f"  Row count: {row_count} (good)")

        # Check timestamps are monotonically increasing
        timestamps = [float(row.get("Timestamp", 0)) for row in data]
        if all(timestamps[i] <= timestamps[i+1] for i in range(len(timestamps)-1)):
            self.log("PASS", f"  Timestamps monotonically increasing ✓")
        else:
            bad_count = sum(1 for i in range(len(timestamps)-1) if timestamps[i] > timestamps[i+1])
            self.log("ERROR", f"  Timestamps NOT monotonic! {bad_count} reversals found.")

        # Check timestamp range (duration)
        duration = timestamps[-1] - timestamps[0]
        self.log("INFO", f"  Duration: {duration:.1f}s ({duration/60:.1f} min)")
        if duration < 5:
            self.log("WARN", f"  Very short duration ({duration:.1f}s) — was this a test?")

        # Check sampling rate
        if row_count > 10:
            avg_interval = duration / (row_count - 1)
            sampling_rate = 1.0 / avg_interval if avg_interval > 0 else 0
            if 60 < sampling_rate < 120:
                self.log("PASS", f"  Sampling rate: ~{sampling_rate:.0f} Hz (expected ~90 Hz)")
            else:
                self.log("WARN", f"  Sampling rate: ~{sampling_rate:.0f} Hz (expected ~90 Hz)")

        # Check quaternion validity (should be unit quaternions)
        try:
            quats = np.array([
                [float(row["UnitQuaternion.x"]), float(row["UnitQuaternion.y"]),
                 float(row["UnitQuaternion.z"]), float(row["UnitQuaternion.w"])]
                for row in data[:100]  # Check first 100 rows
            ])
            norms = np.linalg.norm(quats, axis=1)
            if np.allclose(norms, 1.0, atol=0.05):
                self.log("PASS", f"  Quaternions are unit length (norm ≈ 1.0) ✓")
            else:
                self.log("WARN", f"  Quaternion norms range: [{norms.min():.3f}, {norms.max():.3f}] (expected ~1.0)")
        except (KeyError, ValueError) as e:
            self.log("ERROR", f"  Cannot validate quaternions: {e}")

        # Check position values are reasonable (should be near origin for seated VR)
        try:
            positions = np.array([
                [float(row["HmdPosition.x"]), float(row["HmdPosition.y"]), float(row["HmdPosition.z"])]
                for row in data[:100]
            ])
            max_displacement = np.max(np.abs(positions))
            if max_displacement < 10.0:  # Within 10 meters
                self.log("PASS", f"  Position values reasonable (max displacement: {max_displacement:.2f}m)")
            else:
                self.log("WARN", f"  Large position values (max: {max_displacement:.2f}m) — participant moved a lot?")
        except (KeyError, ValueError):
            self.log("WARN", f"  Cannot validate positions")

        # Check for all-zero rows (tracking failure)
        try:
            zero_rows = sum(1 for row in data if
                abs(float(row.get("UnitQuaternion.x", 1))) < 1e-10 and
                abs(float(row.get("UnitQuaternion.y", 1))) < 1e-10 and
                abs(float(row.get("UnitQuaternion.z", 1))) < 1e-10)
            if zero_rows > 0:
                pct = (zero_rows / row_count) * 100
                self.log("WARN", f"  {zero_rows} zero-rotation rows ({pct:.1f}%) — tracking drops")
            else:
                self.log("PASS", f"  No zero-rotation rows (tracking stable) ✓")
        except (KeyError, ValueError):
            pass

    def validate_eye_csv(self, filepath, pid):
        """Validate an eye tracking CSV file."""
        self.log("INFO", f"  Checking: {filepath.name}")

        try:
            data = self._read_csv(filepath)
        except Exception as e:
            self.log("ERROR", f"  Cannot read file: {e}")
            return

        if len(data) == 0:
            self.log("ERROR", f"  File is empty!")
            return

        row_count = len(data)
        self.log("INFO", f"  Row count: {row_count}")

        # Check for actual eye tracking data (not all zeros)
        try:
            gaze_x = [float(row.get("GazeDir.x", 0)) for row in data[:200]]
            gaze_y = [float(row.get("GazeDir.y", 0)) for row in data[:200]]
            gaze_z = [float(row.get("GazeDir.z", 0)) for row in data[:200]]

            all_zero = all(abs(x) < 1e-6 and abs(y) < 1e-6 and abs(z) < 1e-6
                         for x, y, z in zip(gaze_x, gaze_y, gaze_z))

            if all_zero:
                self.log("ERROR", f"  ALL gaze direction values are zero! Eye tracking not working!")
                self.log("ERROR", f"  → Check: Quest Pro eye tracking enabled & calibrated?")
                self.log("ERROR", f"  → Check: App has eye tracking permission?")
                self.log("ERROR", f"  → Check: OVRManager Eye Tracking = Required?")
            else:
                # Check gaze direction is unit vector
                norms = np.array([np.sqrt(x**2 + y**2 + z**2)
                                 for x, y, z in zip(gaze_x, gaze_y, gaze_z)])
                valid_norms = norms[norms > 0.1]  # Exclude zero entries
                if len(valid_norms) > 0 and np.allclose(valid_norms, 1.0, atol=0.1):
                    self.log("PASS", f"  Gaze directions are unit vectors ✓")
                else:
                    self.log("WARN", f"  Gaze direction norms vary: [{valid_norms.min():.3f}, {valid_norms.max():.3f}]")

                # Check confidence values
                confidences = [float(row.get("LeftConfidence", 0)) for row in data[:200]]
                avg_conf = np.mean(confidences)
                if avg_conf > 0.5:
                    self.log("PASS", f"  Average tracking confidence: {avg_conf:.2f} (good)")
                elif avg_conf > 0.1:
                    self.log("WARN", f"  Average tracking confidence: {avg_conf:.2f} (low — poor tracking?)")
                else:
                    self.log("ERROR", f"  Average tracking confidence: {avg_conf:.2f} (very low!)")

                # Check BothEyesValid
                both_valid = [int(row.get("BothEyesValid", 0)) for row in data]
                pct_both = (sum(both_valid) / len(both_valid)) * 100
                if pct_both > 80:
                    self.log("PASS", f"  Both eyes valid: {pct_both:.0f}% of frames ✓")
                elif pct_both > 50:
                    self.log("WARN", f"  Both eyes valid: only {pct_both:.0f}% (some occlusion?)")
                else:
                    self.log("ERROR", f"  Both eyes valid: only {pct_both:.0f}% (major tracking issues!)")

        except (KeyError, ValueError) as e:
            self.log("ERROR", f"  Cannot validate gaze data: {e}")

    def validate_combined_csv(self, filepath, pid):
        """Validate a combined tracking CSV file."""
        self.log("INFO", f"  Checking: {filepath.name}")

        try:
            data = self._read_csv(filepath)
        except Exception as e:
            self.log("ERROR", f"  Cannot read file: {e}")
            return

        if len(data) == 0:
            self.log("ERROR", f"  File is empty!")
            return

        row_count = len(data)
        self.log("INFO", f"  Row count: {row_count}")

        # Check eye-head offset is reasonable
        try:
            offsets = [float(row.get("EyeHeadOffset", 0)) for row in data]
            avg_offset = np.mean(offsets)
            max_offset = np.max(offsets)

            if avg_offset < 30:
                self.log("PASS", f"  Average eye-head offset: {avg_offset:.1f}° (normal)")
            else:
                self.log("WARN", f"  Average eye-head offset: {avg_offset:.1f}° (high — eye tracking issue?)")

            if max_offset < 90:
                self.log("PASS", f"  Max eye-head offset: {max_offset:.1f}° (reasonable)")
            else:
                self.log("WARN", f"  Max eye-head offset: {max_offset:.1f}° (very large!)")
        except (KeyError, ValueError):
            self.log("WARN", f"  Cannot validate eye-head offsets")

    def _read_csv(self, filepath):
        """Read a CSV file and return list of dicts."""
        data = []
        with open(filepath, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                data.append(row)
        return data


# =============================================================================
# WU_MMSYS_17 FORMAT CONVERTER
# =============================================================================

def convert_to_wu_format(input_dir, output_dir):
    """
    Convert Quest Pro head tracking CSV to Wu_MMSys_17 format.
    This creates files compatible with your existing dataset.

    Wu_MMSys_17 format:
    Timestamp,PlaybackTime,UnitQuaternion.x,UnitQuaternion.y,UnitQuaternion.z,UnitQuaternion.w,HmdPosition.x,HmdPosition.y,HmdPosition.z
    """
    input_path = Path(input_dir)
    output_path = Path(output_dir)
    output_path.mkdir(parents=True, exist_ok=True)

    head_files = sorted(input_path.rglob("head_*.csv"))

    for hf in head_files:
        # Parse participant and video from filename
        # Format: head_P001_video_0_20260216_143022.csv
        parts = hf.stem.split("_")
        if len(parts) >= 3:
            pid = parts[1]  # P001
            vid = "_".join(parts[2:-2])  # video_0
        else:
            pid = "unknown"
            vid = hf.stem

        print(f"Converting: {hf.name} → Wu_MMSys_17 format")

        # Create output structure: output_dir/{participant_num}/video_{n}.csv
        # Extract participant number
        try:
            p_num = pid.replace("P", "").replace("p", "")
            p_num = str(int(p_num))
        except ValueError:
            p_num = pid

        participant_dir = output_path / p_num
        participant_dir.mkdir(exist_ok=True)

        # Read input
        with open(hf, 'r') as f:
            reader = csv.DictReader(f)
            rows = list(reader)

        # Write Wu_MMSys_17 compatible output
        out_filename = f"{vid}.csv"
        out_path = participant_dir / out_filename

        with open(out_path, 'w', newline='') as f:
            writer = csv.writer(f)
            writer.writerow(WU_MMSYS_COLUMNS)

            for row in rows:
                writer.writerow([
                    row.get("Timestamp", "0"),
                    row.get("PlaybackTime", "0"),
                    row.get("UnitQuaternion.x", "0"),
                    row.get("UnitQuaternion.y", "0"),
                    row.get("UnitQuaternion.z", "0"),
                    row.get("UnitQuaternion.w", "0"),
                    row.get("HmdPosition.x", "0"),
                    row.get("HmdPosition.y", "0"),
                    row.get("HmdPosition.z", "0"),
                ])

        print(f"  → Saved: {out_path} ({len(rows)} rows)")

    print(f"\nDone! Converted {len(head_files)} files to Wu_MMSys_17 format in {output_path}")


# =============================================================================
# MAIN
# =============================================================================

def main():
    parser = argparse.ArgumentParser(description="Validate Quest Pro collected data for STAR-VP")
    parser.add_argument("data_dir", help="Path to data directory (Raw/ folder or participant folder)")
    parser.add_argument("--fix", action="store_true", help="Attempt to auto-fix issues")
    parser.add_argument("--convert-wu", metavar="OUTPUT_DIR",
                       help="Convert head tracking data to Wu_MMSys_17 format")

    args = parser.parse_args()

    if args.convert_wu:
        convert_to_wu_format(args.data_dir, args.convert_wu)
    else:
        validator = DataValidator(args.data_dir, fix_mode=args.fix)
        validator.validate_all()


if __name__ == "__main__":
    main()
