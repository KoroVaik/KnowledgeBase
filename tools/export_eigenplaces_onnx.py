import os
import sys

# Ensure UTF-8 stdout on Windows so emoji print in torch.onnx doesn't crash cp1252
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

import onnx
import torch

def export():
    hub_dir = torch.hub.get_dir()
    os.makedirs(hub_dir, exist_ok=True)
    trusted_file = os.path.join(hub_dir, "trusted_list")
    existing_trusted = set()
    if os.path.exists(trusted_file):
        with open(trusted_file, "r") as f:
            existing_trusted = {line.strip() for line in f}
    
    needed = {"gmberton_eigenplaces", "gmberton_cosplace"}
    to_add = needed - existing_trusted
    if to_add:
        with open(trusted_file, "a") as f:
            for item in to_add:
                f.write(item + "\n")

    output_dir = os.path.expandvars(r"%LOCALAPPDATA%\KnowledgeBase\models\eigenplaces")
    os.makedirs(output_dir, exist_ok=True)
    output_path = os.path.join(output_dir, "eigenplaces_resnet50_512.onnx")

    print("Loading EigenPlaces ResNet50 fc_output_dim=512 via torch.hub...")
    model = torch.hub.load(
        "gmberton/eigenplaces",
        "get_trained_model",
        backbone="ResNet50",
        fc_output_dim=512,
        trust_repo=True
    )
    model.eval()

    dummy = torch.randn(1, 3, 512, 512)
    print(f"Exporting ONNX to {output_path}...")
    torch.onnx.export(
        model,
        dummy,
        output_path,
        export_params=True,
        opset_version=18,
        input_names=["image"],
        output_names=["descriptor"],
        dynamic_axes={"image": {0: "batch"}, "descriptor": {0: "batch"}}
    )

    # Consolidate external data into a single self-contained ONNX file if needed
    data_file = output_path + ".data"
    if os.path.exists(data_file):
        print("Consolidating model into single self-contained ONNX file...")
        loaded = onnx.load(output_path, load_external_data=True)
        onnx.save(loaded, output_path, save_as_external_data=False)
        try:
            os.remove(data_file)
        except OSError:
            pass

    print("Export complete!")

if __name__ == "__main__":
    export()
