extends SceneTree

func _initialize():
	root.add_child.call_deferred(load("res://benchmarks/MapStorageBenchmark.cs").new())
